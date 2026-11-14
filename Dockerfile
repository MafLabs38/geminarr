# syntax=docker/dockerfile:1

# The base images are pinned by digest (the tag says what they are, the digest what they were when a release was built); Dependabot moves both.
# Tag and digest together are on purpose, an exception to SonarQube's docker:S8431 stated in ci.yml and in Mnemarr's ADR 0013: do not "fix" it.

# The build stages run on the builder's own architecture (their output does not depend on the target one): only the last stage is
# per architecture, and it only copies files, so a multi-architecture image is built without emulation.
# The constraint that comes with it: an asset that differs per architecture (a native library) must be chosen with ARG TARGETARCH
# (or `dotnet publish -a`), never by the architecture of the builder, or the arm64 image gets amd64 files and fails only when it runs.
# The browser of the session (Chromium, installed in the last stage) is such an asset.
FROM --platform=$BUILDPLATFORM node:22-alpine@sha256:fe4f6d74e20370acb04ad889077d14d0f64a9c8ea6a42bc60cddf1e844699914 AS frontend
WORKDIR /build
COPY package.json yarn.lock ./
# No install script runs while the interface is built: the dev tools of the lockfile (test runners, mutation tools) are not needed by the build and would
# run code in the stage that produces what the image ships (as in the release job).
RUN corepack enable && yarn install --frozen-lockfile --ignore-scripts
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
COPY src/Geminarr.Browser/Geminarr.Browser.csproj src/Geminarr.Browser/
RUN dotnet restore src/Geminarr.Host/Geminarr.Host.csproj
COPY src ./src
RUN dotnet publish src/Geminarr.Host/Geminarr.Host.csproj -c Release --no-restore -o /app -p:Version=${VERSION}

# The final stage is Ubuntu, not the chiseled image: the browser of the session (Chromium, driven by Playwright) needs its system libraries, installed here with the browser itself.
# This is the one stage that runs a command in the target architecture, so a multi-architecture build needs QEMU (the image job of ci.yml sets it up) or a builder of each architecture.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /build/_output/UI ./wwwroot
# Chromium (the headless shell only: there is no screen) and the libraries and fonts it needs, installed by Playwright's own driver, which the application ships for every platform: only the one of
# this architecture is kept. The browser lives outside the user's home and is readable by everyone; it is the profile, a volume of its own, that holds the session.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
# The data folder and the profile folder are mount points owned by the application's user: the container must be able to write them (SonarQube hotspot docker:S6504, reviewed as safe: no other file of the
# image is writable by the application). The profile is private to its owner.
RUN set -eux; \
    case "$(uname -m)" in \
      x86_64) platform=linux-x64 ;; \
      aarch64) platform=linux-arm64 ;; \
      *) echo "unsupported architecture" >&2; exit 1 ;; \
    esac; \
    chmod 755 ./.playwright/node/${platform}/node; \
    ./.playwright/node/${platform}/node ./.playwright/package/cli.js install --with-deps --only-shell chromium; \
    find ./.playwright/node -mindepth 1 -maxdepth 1 ! -name "${platform}" -exec rm -rf {} +; \
    rm -rf /var/lib/apt/lists/* /root/.cache; \
    chmod -R a+rX /ms-playwright ./.playwright; \
    mkdir /config /profile; \
    chown 1654:1654 /config /profile; \
    chmod 700 /profile
ENV GEMINARR__DATAFOLDER=/config
ENV GEMINARR__BROWSER__PROFILEFOLDER=/profile
# The browser writes a few small files outside its profile (crash reports, a cache): in the temporary folder, which a read-only root file system keeps writable.
ENV HOME=/tmp
# The base image sets ASPNETCORE_HTTP_PORTS=8080, which the Urls setting of appsettings.json overrides with a warning at every start (change the address with GEMINARR__URLS).
ENV ASPNETCORE_HTTP_PORTS=
# The app user of the image, stated so that nothing reads this image as running as root.
USER 1654:1654
VOLUME ["/config", "/profile"]
EXPOSE 8282
ENTRYPOINT ["dotnet", "Geminarr.dll"]
