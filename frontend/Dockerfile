# ── Build stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

COPY fanpad-dashboard/package*.json ./
RUN npm ci

COPY fanpad-dashboard/ .
RUN npm run build -- --configuration production

# ── Serve stage ───────────────────────────────────────────────────────────────
FROM nginx:alpine AS runtime
COPY --from=build /app/dist/fanpad-dashboard/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
