ARG NODE_VERSION=24

FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

WORKDIR /solution

COPY . .

RUN dotnet publish --runtime linux-x64

FROM node:${NODE_VERSION}-trixie-slim AS node

FROM debian:trixie-slim AS steamcmd-installer

ARG DEBIAN_FRONTEND=noninteractive

# Valve distributes the x64 client through a 32-bit self-updating bootstrapper.
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl lib32gcc-s1; \
    install -m 0755 -d /opt/steamcmd; \
    curl -fsSL "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz" -o /tmp/steamcmd.tar.gz; \
    tar -xzf /tmp/steamcmd.tar.gz -C /opt/steamcmd; \
    rm -f /tmp/steamcmd.tar.gz; \
    cd /opt/steamcmd; \
    status=0; \
    ./steamcmd.sh +quit || status=$?; \
    test "$status" -eq 0 -o "$status" -eq 139; \
    test -x /opt/steamcmd/linux64/steamcmd; \
    /opt/steamcmd/linux64/steamcmd +quit

FROM debian:trixie-slim

ARG DEBIAN_FRONTEND=noninteractive
ARG TOOL_TIMEOUT=14400

USER root
WORKDIR /var/workspace

ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
ENV COMPOSER_ALLOW_SUPERUSER=1
ENV CI_TOOLS_CALL_TIMEOUT=86400

# Package sources
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    curl -fsSLo /tmp/debsuryorg-archive-keyring.deb \
        https://packages.sury.org/debsuryorg-archive-keyring.deb && \
    dpkg -i /tmp/debsuryorg-archive-keyring.deb && \
    rm -f /tmp/debsuryorg-archive-keyring.deb && \
    echo "deb [signed-by=/usr/share/keyrings/debsuryorg-archive-keyring.gpg] https://packages.sury.org/php/ trixie main" \
        > /etc/apt/sources.list.d/php.list

# Runtime tools
COPY linux/ci-tools.packages /tmp/ci-tools.packages
COPY --from=node /usr/local/ /usr/local/

RUN apt-get update && \
    xargs apt-get install -y --no-install-recommends < /tmp/ci-tools.packages && \
    rm -f /tmp/ci-tools.packages && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* && \
    node --version && \
    php --version && \
    php -m | grep -qi '^imap$' && \
    php -m | grep -qi '^xsl$'

# itch.io Butler
RUN curl -fsSL "https://broth.itch.zone/butler/linux-amd64/LATEST/archive/default" -o /tmp/butler.zip && \
    unzip /tmp/butler.zip -d /tmp/butler && \
    install -m 0755 /tmp/butler/butler /usr/local/bin/butler && \
    rm -rf /tmp/butler /tmp/butler.zip && \
    butler --version

# SteamCMD
COPY --from=steamcmd-installer /opt/steamcmd /opt/steamcmd
RUN ln -s /opt/steamcmd/linux64/steamcmd /usr/local/bin/steamcmd && \
    steamcmd +quit

# Composer-backed CI tools
COPY --chmod=755 --from=composer:2 /usr/bin/composer /usr/local/bin/composer
COPY --chmod=755 --from=build /solution/common/CiTools/bin/Release/net9.0/linux-x64/publish/ci-tools-launcher /ci-tools/ci-tools-launcher

RUN cd /ci-tools && \
    composer require "slothsoft/unity" --no-interaction && \
    composer config process-timeout "${TOOL_TIMEOUT}" && \
    composer update --no-interaction --no-dev --optimize-autoloader --classmap-authoritative && \
    composer check-platform-reqs --no-dev && \
    ln -s /ci-tools/ci-tools-launcher /usr/local/bin/steam-buildfile && \
    ln -s /ci-tools/ci-tools-launcher /usr/local/bin/steam-login && \
    steam-buildfile | grep -q '^Create a steam build file\.' && \
    steam-login unexpected-argument | grep -q '^Sign in to Steam via steamcmd\.'

VOLUME ["/root/Steam"]
