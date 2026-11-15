FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.18 as builder
# hadolint ignore=DL3018
RUN apk add --no-cache tzdata icu-libs ca-certificates && update-ca-certificates
ARG TARGETARCH
RUN addgroup -g 1000 dotnet  && adduser -G dotnet -u 1000 dotnet -D
USER dotnet
WORKDIR /app
COPY --chown=dotnet "App/App.csproj" "App/"
RUN dotnet restore -a $TARGETARCH "App/App.csproj"
COPY --chown=dotnet . .
WORKDIR /app
RUN dotnet tool install --global dotnet-ef --version 8.0.0
ENV PATH="$PATH:/home/dotnet/.dotnet/tools"
ENV LANDSCAPE=lapras
RUN case "$TARGETARCH" in \
      amd64) RID=linux-musl-x64 ;; \
      arm64) RID=linux-musl-arm64 ;; \
      *) echo "Unsupported target architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac && dotnet-ef migrations bundle --project ./App --target-runtime "$RID"
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
CMD [ "./efbundle" ]
