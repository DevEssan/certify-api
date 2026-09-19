FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CertifyApi/CertifyApi.csproj src/CertifyApi/
RUN dotnet restore src/CertifyApi/CertifyApi.csproj
COPY src/CertifyApi/ src/CertifyApi/
RUN dotnet publish src/CertifyApi/CertifyApi.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CertifyApi.dll"]