FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build

ARG TARGETARCH

WORKDIR /solution

COPY . .

RUN set -eux; \
    case "$TARGETARCH" in \
      amd64) runtime_id="linux-x64" ;; \
      arm64) runtime_id="linux-arm64" ;; \
      *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    dotnet publish \
        --configuration Release \
        --runtime "$runtime_id" \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:UseAppHost=true \
        --property:DebugType=None

FROM debian:trixie-slim AS steamcmd-installer

ARG DEBIAN_FRONTEND=noninteractive

# Valve distributes the x64 client through a 32-bit self-updating bootstrapper.
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl lib32gcc-s1 qemu-user; \
    install -m 0755 -d /opt/steamcmd; \
    curl -fsSL "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz" -o /tmp/steamcmd.tar.gz; \
    tar -xzf /tmp/steamcmd.tar.gz -C /opt/steamcmd; \
    rm -f /tmp/steamcmd.tar.gz; \
    bootstrap_status=0; \
    (cd /opt/steamcmd && DEBUGGER=qemu-i386 ./steamcmd.sh +quit) || bootstrap_status="$?"; \
    case "$bootstrap_status" in 0|139) ;; *) exit "$bootstrap_status" ;; esac; \
    test -x /opt/steamcmd/linux64/steamcmd; \
    cd /opt/steamcmd; \
    ./linux64/steamcmd +quit

FROM debian:trixie-slim

ARG DEBIAN_FRONTEND=noninteractive

USER root

COPY linux/machine-id /etc/machine-id

WORKDIR /var/workspace

# Unity Licensing Client uses .NET globalization and fails fast without ICU.
# Base tools + locale
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        git-lfs \
        gnupg \
        locales \
        nano \
        pciutils \
        util-linux \
        wget \
        zip \
        unzip \
        xz-utils \
        libxfixes3 \
        libxi6 \
        libxxf86vm1 \
        libxrender1 \
        libxkbcommon0 \
        libsm6 \
        libice6 \
        libgl1 \
        libegl1 \
        libdbus-1-3 \
        libfontconfig1 \
        libfreetype6 \
        libicu76 && \
    echo "$LANG UTF-8" > /etc/locale.gen && \
    locale-gen "$LANG" && \
    install -m 0755 -d /etc/apt/keyrings && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* && \
    git config --global --add safe.directory * && \
    curl --version && \
    ldconfig -p | grep -q 'libicuuc\.so'

# NodeJS
ARG NODE_VERSION=24
ENV NODE_VERSION="${NODE_VERSION}"
RUN curl -fsSL "https://deb.nodesource.com/setup_${NODE_VERSION}.x" | bash - && \
    apt-get install -y --no-install-recommends nodejs && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* && \
    node --version && \
    test "$(node --version | cut -d. -f1)" = "v${NODE_VERSION}" && \
    npm --version

# itch.io
RUN curl -fsSL "https://broth.itch.zone/butler/linux-amd64/LATEST/archive/default" -o /tmp/butler.zip && \
    unzip /tmp/butler.zip -d /tmp/butler && \
    chmod +x /tmp/butler/butler && \
    mv /tmp/butler/butler /usr/local/bin/butler && \
    rm -rf /tmp/butler /tmp/butler.zip && \
    butler --help

# Steam SDK
COPY --from=steamcmd-installer /opt/steamcmd /opt/steamcmd
RUN ln -s /opt/steamcmd/linux64/steamcmd /usr/local/bin/steamcmd && \
    steamcmd +quit

# PHP
ENV PHP_VERSION=8.4
RUN curl -fsSLo /tmp/debsuryorg-archive-keyring.deb \
        https://packages.sury.org/debsuryorg-archive-keyring.deb && \
    dpkg -i /tmp/debsuryorg-archive-keyring.deb && \
    rm /tmp/debsuryorg-archive-keyring.deb && \
    echo "deb [signed-by=/usr/share/keyrings/debsuryorg-archive-keyring.gpg] https://packages.sury.org/php/ trixie main" \
        > /etc/apt/sources.list.d/php.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends \
        php8.4-cli \
        php8.4-common \
        php8.4-curl \
        php8.4-imap \
        php8.4-intl \
        php8.4-mbstring \
        php8.4-xml \
        php8.4-zip && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* && \
	php --version && \
    test "$(php -r 'echo PHP_MAJOR_VERSION, ".", PHP_MINOR_VERSION;')" = "$PHP_VERSION" && \
    php -m | grep -qi '^imap$'

# .NET SDK
ARG DOTNET_VERSION=9.0
ENV DOTNET_VERSION="${DOTNET_VERSION}"
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DOTNET_CLI_UI_LANGUAGE=en
ENV PATH="/root/.dotnet/tools:${PATH}"
RUN curl -fsSL https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb && \
    dpkg -i /tmp/packages-microsoft-prod.deb && \
    rm /tmp/packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y --no-install-recommends "dotnet-sdk-${DOTNET_VERSION}" && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* && \
    dotnet --version && \
    test "$(dotnet --version | cut -d. -f1-2)" = "$DOTNET_VERSION"

# DocFX
RUN dotnet tool update -g docfx && \
    docfx --version

# Blender
ARG BLENDER_SERIES=4.5

RUN set -eux; \
    arch="$(dpkg --print-architecture)"; \
    case "$arch" in \
      amd64) blender_arch="x64" ;; \
      arm64) blender_arch="arm64" ;; \
      *) echo "Unsupported architecture: $arch" >&2; exit 1 ;; \
    esac; \
    base_url="https://download.blender.org/release/Blender${BLENDER_SERIES}/"; \
    version="$(curl -fsSL "$base_url" \
      | grep -oE "blender-${BLENDER_SERIES}\.[0-9]+-linux-${blender_arch}\.tar\.xz" \
      | sed -E "s/^blender-([0-9]+\.[0-9]+\.[0-9]+)-linux-.*$/\1/" \
      | sort -V \
      | tail -n1)"; \
    test -n "$version"; \
    curl -fsSL "${base_url}blender-${version}-linux-${blender_arch}.tar.xz" -o /tmp/blender.tar.xz; \
    rm -rf /opt/blender-*; \
    tar -xJf /tmp/blender.tar.xz -C /opt; \
    ln -sfn "/opt/blender-${version}-linux-${blender_arch}/blender" /usr/local/bin/blender; \
    rm -f /tmp/blender.tar.xz; \
    blender --version

# Unity Hub
RUN install -m 0755 -d /etc/apt/keyrings && \
    curl -fsSL https://hub.unity3d.com/linux/keys/public | gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg && \
    chmod a+r /etc/apt/keyrings/unityhub.gpg && \
    echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" \
        > /etc/apt/sources.list.d/unityhub.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends \
        cpio \
        desktop-file-utils \
        ffmpeg \
        libasound2 \
        libc6-dev \
        libgbm-dev \
        libglu1-mesa \
        p7zip-full \
        python3 \
        unityhub=3.12.1 \
        xauth \
        xvfb \
        xz-utils && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Legacy Unity editor compatibility. These libraries were removed in Trixie,
# so only their Bookworm packages are sourced from Debian Snapshot.
ARG BOOKWORM_SNAPSHOT=20260615T025018Z
RUN echo "deb [check-valid-until=no] https://snapshot.debian.org/archive/debian/${BOOKWORM_SNAPSHOT}/ bookworm main" \
        > /etc/apt/sources.list.d/bookworm.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends libgconf-2-4 libncurses5 libtinfo5 && \
    rm /etc/apt/sources.list.d/bookworm.list && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Unity 2021 OpenSSL compatibility. Bullseye security is retained only for the
# legacy library, whose final package is available from Debian Snapshot.
ARG BULLSEYE_SECURITY_SNAPSHOT=20260615T025018Z
RUN echo "deb [check-valid-until=no] https://snapshot.debian.org/archive/debian-security/${BULLSEYE_SECURITY_SNAPSHOT}/ bullseye-security main" \
        > /etc/apt/sources.list.d/bullseye-security.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends libssl1.1 && \
    rm /etc/apt/sources.list.d/bullseye-security.list && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Farah
ENV COMPOSE_UNITY="compose-unity"
ENV COMPOSER_ALLOW_SUPERUSER="1"

ENV UNITY_LOGGING="stdin stdout stderr"
ENV UNITY_ACCELERATOR_ENDPOINT=""
ENV UNITY_NO_GRAPHICS="1"

COPY --chmod=755 --from=composer:2 /usr/bin/composer /usr/local/bin/composer
COPY --chmod=755 --from=build /solution/common/ComposeUnity/bin/Release/net9.0/linux-*/publish/compose-unity /usr/local/bin/compose-unity
RUN ln -s compose-unity /usr/local/bin/compose-unity-sidecar

ENV COMPOSE_UNITY_CALL_TIMEOUT="86400"
ENV COMPOSE_UNITY_MCP="0"

ARG UNITY_TIMEOUT="14400"
ARG SLOTHSOFT_UNITY_VERSION=2.22
RUN install -m 0755 -d /compose-unity && \
    compose-unity require "slothsoft/unity:^${SLOTHSOFT_UNITY_VERSION}" && \
    compose-unity config process-timeout "$UNITY_TIMEOUT" && \
    compose-unity update --no-interaction --no-dev --optimize-autoloader --classmap-authoritative

# Test
RUN compose-unity exec unity-build && \
    compose-unity exec unity-help && \
    rm -rf /run/compose-unity

VOLUME ["/root/Unity", "/root/Steam", "/root/.config/unity3d", "/root/.config/unityhub", "/root/.cache/Unity", "/root/.local/share/unity3d"]

EXPOSE 8080

HEALTHCHECK --interval=60s --timeout=10s --start-period=30s --retries=3 \
    CMD ["compose-unity", "sidecar", "health"]

CMD ["compose-unity", "sidecar"]
