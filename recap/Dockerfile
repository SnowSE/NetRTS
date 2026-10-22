# The nightly recap job. Playwright's image brings Chromium and its fonts; ffmpeg encodes the video.
# Keep the tag in step with playwright-core in package.json.
FROM mcr.microsoft.com/playwright:v1.63.0-noble

RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg fonts-roboto \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY src ./src
COPY show ./show

USER pwuser
ENV NODE_ENV=production
CMD ["node", "src/index.mjs", "--out", "/tmp/recap"]
