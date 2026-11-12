# syntax=docker/dockerfile:1

# ---------- Stage 1: build ----------
# Run the SDK natively on the build machine ($BUILDPLATFORM) and cross-compile
# for the target architecture, so building an arm64 image on x64 needs no emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy only the project file first: this layer (restore) is cached
# and re-run only when the dependencies change, not on every code edit.
COPY SchoolLibrary.Web.csproj ./
RUN dotnet restore -a $TARGETARCH

COPY . ./
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app/publish /p:UseAppHost=false

# ---------- Stage 2: runtime ----------
# The runtime image has no SDK, so the final image is much smaller.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Directory for Data Protection keys (mounted as a volume). Created as root,
# then handed to the unprivileged user that the aspnet image provides ($APP_UID).
RUN mkdir /keys && chown $APP_UID /keys

COPY --from=build /app/publish ./

# The aspnet:10.0 image already listens on port 8080 (ASPNETCORE_HTTP_PORTS=8080),
# so no extra ASPNETCORE_URLS is needed. EXPOSE only documents the port.
EXPOSE 8080

# Never run the app as root inside the container.
USER $APP_UID

ENTRYPOINT ["dotnet", "SchoolLibrary.Web.dll"]
