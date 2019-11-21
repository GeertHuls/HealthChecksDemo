using AspNetCoreRateLimit;
using HealthChecks.UI.Client;
using InvestmentManager.Core;
using InvestmentManager.DataAccess.EF;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace InvestmentManager
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            this.loggerFactory = loggerFactory;

            // For NLog                   
            NLog.LogManager.LoadConfiguration("nlog.config");
        }

        public IConfiguration Configuration { get; }

        private readonly ILoggerFactory loggerFactory;


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            ConfigureRateLimit(services, Configuration);

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddMvc(options => options.EnableEndpointRouting = false).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
            services.AddSingleton<IConfiguration>(this.Configuration);

            // Configure the data access layer
            var connectionString = this.Configuration.GetConnectionString("InvestmentDatabase");

            services.RegisterEfDataAccessClasses(connectionString, loggerFactory);  

            // For Application Services
            String stockIndexServiceUrl = this.Configuration["StockIndexServiceUrl"];
            services.ConfigureStockIndexServiceHttpClientWithoutProfiler(stockIndexServiceUrl);
            services.ConfigureInvestmentManagerServices(stockIndexServiceUrl);

            // Configure logging
            services.AddLogging(loggingBuilder => {
                loggingBuilder.AddConsole();
                loggingBuilder.AddDebug();
                loggingBuilder.AddNLog();
            });

            var securityLogFilePath = this.Configuration["SecurityLogFilePath"];

            // Dependency health checks:
            services.AddHealthChecks()
                .AddSqlServer(connectionString, failureStatus: HealthStatus.Unhealthy, tags: new [] { "ready" })
                .AddUrlGroup(new Uri($"{stockIndexServiceUrl}/api/StockIndexes"),
                    "Stock index api health check", HealthStatus.Degraded, tags: new [] { "ready" },
                    timeout: new TimeSpan(0, 0, 5))
                .AddFilePathWriter(securityLogFilePath, HealthStatus.Unhealthy, tags: new [] { "ready" });

            services.AddHealthChecksUI();

            services.AddAuthorization(options => {
                options.AddPolicy("HealthCheckPolicy", policy =>
                                  policy.RequireClaim("client_policy", "healthChecks"));
            });

            services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", options =>
                {
                    options.Authority = "http://idserver:50337";
                    options.RequireHttpsMetadata = false;

                    options.Audience = "InvestmentManagerAPI";
                });
        }

        private void ConfigureRateLimit(IServiceCollection services, IConfiguration configuration)
        {
            // needed to load configuration from appsettings.json
            services.AddOptions();
            // needed to store rate limit counters and ip rules
            services.AddMemoryCache();
            //load general configuration from appsettings.json
            services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
            //load ip rules from appsettings.json
            services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));
            // inject counter and rules stores
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            // Add framework services.
            // services.AddMvc();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            // configuration (resolvers, counter key builders)
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        }

        // Configures the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            //app.UseHealthChecks("/health"); .NET Core 2.2

            app.UseIpRateLimiting();
            app.UseDeveloperExceptionPage();
            
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions()
                {
                    ResultStatusCodes = {
                        [HealthStatus.Healthy] = StatusCodes.Status200OK,
                        [HealthStatus.Degraded] = StatusCodes.Status500InternalServerError,
                        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                    },
                    ResponseWriter = WriteHealthCheckReadyResponse,
                    Predicate = (check) => check.Tags.Contains("ready"),
                    AllowCachingResponses = false
                }); // Enable authentication: .RequireAuthorization();

                endpoints.MapHealthChecks("health/live", new HealthCheckOptions()
                {
                    Predicate = (check) => !check.Tags.Contains("ready"),
                    ResponseWriter = WriteHealthCheckLiveResponse,
                    AllowCachingResponses = false
                }) // Enable authentication: .RequireAuthorization("HealthCheckPolicy"); // --> refers to policy defined above
                // .RequireCors(builder => {
                //     builder.WithOrigins("http://example.com", "http://example2.com");
                //     // or
                //     //builder.AllowAnyOrigin();
                // })
                ;

                endpoints.MapHealthChecks("healthui", new HealthCheckOptions()
                {
                    Predicate = _ => true,
                    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
                }); // Enable authentication: .RequireAuthorization();

                // use RequireHost in case the health endpoint
                // should only be visible on a certain host:
                //  .RequireHost("*:5000");
            });

            // The health checks ui is located at: http://localhost:???/healthchecks-ui
            app.UseHealthChecksUI();
        }

        private Task WriteHealthCheckLiveResponse(HttpContext httpContext, HealthReport result)
        {
            httpContext.Response.ContentType = "application/json";
            var json = new JObject(
                    new JProperty("OverallStatus", result.Status.ToString()),
                    new JProperty("TotalChecksDuration", result.TotalDuration.TotalSeconds.ToString("0:0.00"))
                );

            return httpContext.Response.WriteAsync(json.ToString(Formatting.Indented));
        }

        private Task WriteHealthCheckReadyResponse(HttpContext httpContext, HealthReport result)
        {
            httpContext.Response.ContentType = "application/json";
            var json = new JObject(
                    new JProperty("OverallStatus", result.Status.ToString()),
                    new JProperty("TotalChecksDuration", result.TotalDuration.TotalSeconds.ToString("0:0.00")),
                    new JProperty("DependencyHealthChecks", new JObject(
                        result.Entries.Select(d =>
                            new JProperty(d.Key, new JObject(
                                new JProperty("Status", d.Value.Status.ToString()),
                                new JProperty("Duration", d.Value.Duration.TotalSeconds.ToString("0:0.00")),
                                new JProperty("Exception", d.Value.Exception?.Message),
                                new JProperty("Data", new JObject(
                                    d.Value
                                    .Data
                                    .Select(p => new JProperty(p.Key, p.Value))
                                )
                            )
                        )
                    ))
                )));

            return httpContext.Response.WriteAsync(json.ToString(Formatting.Indented));
        }
    }
}
