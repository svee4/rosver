FROM mcr.microsoft.com/dotnet/sdk:10.0 as sdk

WORKDIR /app

# Run from repo root!

COPY . .
WORKDIR /app/src/Rosver

RUN dotnet restore
RUN dotnet publish -c Release -o out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 as runtime

WORKDIR /app
COPY --from=sdk /app/src/Rosver/out .

RUN useradd -ms /bin/bash dockeruser

RUN mkdir /volume
RUN chown -R dockeruser:dockeruser /volume

USER dockeruser:dockeruser

ENTRYPOINT ["dotnet", "Rosver.dll"]