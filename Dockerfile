# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5047

# Stage 2: Build environment
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["WeatherAPI.csproj", "./"]
RUN dotnet restore "WeatherAPI.csproj"
COPY . .
RUN dotnet build "WeatherAPI.csproj" -c Release -o /app/build

# Stage 3: Publish optimized output
FROM build AS publish
RUN dotnet publish "WeatherAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 4: Final production image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5047
ENTRYPOINT ["dotnet", "WeatherAPI.dll"]