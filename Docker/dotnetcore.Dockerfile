FROM mcr.microsoft.com/dotnet/core/sdk:3.0

WORKDIR /home/app

VOLUME [ "/home/app" ]

ENTRYPOINT [ "dotnet" ]

CMD [ "run" ]
