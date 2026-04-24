# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
# Für Native AOT unter Alpine werden Build-Abhängigkeiten benötigt (clang, musl-dev, etc.)
RUN apk add --no-cache clang lld musl-dev build-base zlib-dev

WORKDIR /src

COPY ["Bulb.csproj", "Bulb.csproj"]
RUN dotnet restore "Bulb.csproj"

COPY . .

RUN dotnet publish "Bulb.csproj" \
    -c Release \
    -o /app/publish 

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

RUN apk add --no-cache iptables ip6tables ipvsadm iproute2 kmod nftables

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Bulb.dll"]