FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OrderFlow.slnx ./
COPY src/OrderFlow.Domain/ src/OrderFlow.Domain/
COPY src/OrderFlow.Application/ src/OrderFlow.Application/
COPY src/OrderFlow.Infrastructure/ src/OrderFlow.Infrastructure/
COPY src/OrderFlow.API/ src/OrderFlow.API/
COPY tests/OrderFlow.Tests/ tests/OrderFlow.Tests/
RUN dotnet restore OrderFlow.slnx
RUN dotnet publish src/OrderFlow.API/OrderFlow.API.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=10 CMD curl --fail --silent http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "OrderFlow.API.dll"]
