# syntax=docker/dockerfile:1

# The base images are pinned by digest (the tag says what they are, the digest what they were when a release was built); Dependabot moves both.
# Tag and digest together are on purpose, an exception to SonarQube's docker:S8431 stated in ci.yml and in Mnemarr's ADR 0013: do not "fix" it.

# The build stages run on the builder's own architecture (their output does not depend on the target one): only the last stage is
# per architecture, and it only copies files, so a multi-architecture image is built without emulation.
# The constraint that comes with it: an asset that differs per architecture (a native library) must be chosen with ARG TARGETARCH
# (or `dotnet publish -a`), never by the architecture of the builder, or the arm64 image gets amd64 files and fails only when it runs.
# The browser of the reader (docs/SPEC.md, vertical 2) is such an asset: its stage comes with that vertical.
FROM --platform=$BUILDPLATFORM node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914 AS frontend
WORKDIR /build
COPY package.json yarn.lock ./
RUN corepack enable && yarn install --frozen-lockfile
COPY frontend ./frontend
RUN yarn build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS backend
ARG VERSION=0.0.0-dev
WORKDIR /build
COPY global.json Directory.Build.props Directory.Packages.props ./
# The projects that ship, copied alone first so that packages are restored in a layer a source change does not invalidate.
COPY src/Geminarr.Host/Geminarr.Host.csproj src/Geminarr.Host/
COPY src/Geminarr.Api.V1/Geminarr.Api.V1.csproj src/Geminarr.Api.V1/
COPY src/Geminarr.Core/Geminarr.Core.csproj src/Geminarr.Core/
RUN dotnet restore src/Geminarr.Host/Geminarr.Host.csproj
COPY src ./src
RUN dotnet publish src/Geminarr.Host/Geminarr.Host.csproj -c Release --no-restore -o /app -p:Version=${VERSION} \
    && mkdir /config

# Chiseled image: no shell, no package manager, non-root user by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:c89a6a61f494f20a5ae302990e814627a8d91b332fd4a8e7c1707c2d4da9555d
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /build/_output/UI ./wwwroot
# The chiseled image has no shell to create folders: copy an empty one owned by the app user.
# The folder is owned by the application's user on purpose: it is the mount point of the data volume and the container must be able to write it
# (SonarQube hotspot docker:S6504, reviewed as safe: no other file of the image is writable by the application).
COPY --from=backend --chown=1654:1654 /config /config
ENV GEMINARR__DATAFOLDER=/config
# The base image sets ASPNETCORE_HTTP_PORTS=8080, which the Urls setting of appsettings.json overrides with a warning at every start (change the address with GEMINARR__URLS).
ENV ASPNETCORE_HTTP_PORTS=
# The app user of the chiseled image, stated so that nothing reads this image as running as root.
USER 1654:1654
VOLUME /config
EXPOSE 8282
ENTRYPOINT ["dotnet", "Geminarr.dll"]
