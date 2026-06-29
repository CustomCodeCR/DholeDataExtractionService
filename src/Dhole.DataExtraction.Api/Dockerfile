FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 5205

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj", "src/Dhole.DataExtraction.Api/"]
COPY ["src/Dhole.DataExtraction.Application/Dhole.DataExtraction.Application.csproj", "src/Dhole.DataExtraction.Application/"]
COPY ["src/Dhole.DataExtraction.Contracts/Dhole.DataExtraction.Contracts.csproj", "src/Dhole.DataExtraction.Contracts/"]
COPY ["src/Dhole.DataExtraction.Domain/Dhole.DataExtraction.Domain.csproj", "src/Dhole.DataExtraction.Domain/"]
COPY ["src/Dhole.DataExtraction.Infrastructure/Dhole.DataExtraction.Infrastructure.csproj", "src/Dhole.DataExtraction.Infrastructure/"]
COPY ["src/Dhole.DataExtraction.Persistence/Dhole.DataExtraction.Persistence.csproj", "src/Dhole.DataExtraction.Persistence/"]

RUN dotnet restore "src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj"

COPY . .

RUN dotnet publish "src/Dhole.DataExtraction.Api/Dhole.DataExtraction.Api.csproj" \
  -c Release \
  -o /app/publish \
  /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Development
ENV DOTNET_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:5205

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Dhole.DataExtraction.Api.dll"]
