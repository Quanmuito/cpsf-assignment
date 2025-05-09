﻿FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Development
WORKDIR /src
COPY ["FileStorage.csproj", "./"]
RUN dotnet restore "FileStorage.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet add package AWSSDK.S3 --version 3.7.412.4
RUN dotnet add package AWSSDK.DynamoDBv2 --version 3.7.405.12
RUN dotnet build "FileStorage.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Development
RUN dotnet publish "FileStorage.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FileStorage.dll"]
