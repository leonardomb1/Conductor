FROM alpine:latest

RUN apk add --no-cache \
    icu-libs \
    tzdata \
    curl \
    bash \
    libgcc \
    libintl \
    libssl3 \
    libstdc++ \
    zlib

RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh && \
    chmod +x dotnet-install.sh && \
    ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet && \
    ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

WORKDIR /app

COPY ../../src/ ./src/
COPY ../../*.sln ./

RUN cp /usr/share/zoneinfo/America/Sao_Paulo /etc/localtime && \
    echo "America/Sao_Paulo" > /etc/timezone

ENTRYPOINT ["sh", "-c", "while :; do sleep 2073600; done"]