FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SnagList.Web/SnagList.Web.csproj -c Release -o /app

FROM nginx:alpine AS runtime
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY src/SnagList.Web/nginx.conf /etc/nginx/conf.d/default.conf
