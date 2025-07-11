FROM node:20-alpine AS builder

WORKDIR /app

# Copy package files
COPY ../../../src/Frontend/package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY ../../../src/Frontend/ ./

# Build the application
RUN npm run build

# Production stage
FROM node:20-alpine AS runner

# Install curl for healthchecks
RUN apk add --no-cache curl bash

WORKDIR /app

# Copy built application
COPY --from=builder /app/build ./build
COPY --from=builder /app/package*.json ./
COPY --from=builder /app/node_modules ./node_modules

# Set timezone using environment variable (simpler approach)
ENV TZ=America/Sao_Paulo

# Create non-root user
RUN addgroup -g 1001 -S nodejs && \
    adduser -S svelte -u 1001

# Change ownership
RUN chown -R svelte:nodejs /app
USER svelte

# Expose port
EXPOSE 3000

# Production command
CMD ["node", "build/index.js"]