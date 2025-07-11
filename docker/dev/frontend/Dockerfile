FROM node:20-alpine

# Install curl for healthchecks
RUN apk add --no-cache curl bash

WORKDIR /app

# Copy package files
COPY ../../../src/Frontend/package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY ../../../src/Frontend/ ./

# Set timezone using environment variable (simpler approach)
ENV TZ=America/Sao_Paulo

# Expose port
EXPOSE 3000

# Development command with hot reload
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "3000"]