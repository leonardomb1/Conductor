FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build

WORKDIR /app
COPY ../../src/* ./
RUN dotnet publish -c Debug -r linux-musl-x64 -o pub

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine AS final

WORKDIR /app
COPY --from=build /app/pub/* /app

RUN apk add --no-cache icu-libs tzdata curl && \
    cp /usr/share/zoneinfo/America/Sao_Paulo /etc/localtime && \
    echo "America/Sao_Paulo" > /etc/timezone && \
    chmod +x /app/Conductor

ENTRYPOINT ["/app/Conductor", "-eM"]