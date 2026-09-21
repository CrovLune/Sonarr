#!/bin/bash
# Multi-architecture build and push script for GitHub Container Registry
# Builds for linux/amd64 and linux/arm64 and pushes to ghcr.io

set -e

# Get the directory where the script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"

# Configuration
DOCKER_REGISTRY="ghcr.io/crovlune"
IMAGE_NAME="sonarr"
PLATFORMS="linux/amd64"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Helper functions
log_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

log_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

log_error() {
    echo -e "${RED}❌ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

log_version() {
    echo -e "${CYAN}🏷️  $1${NC}"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."

    # Check if Docker is installed
    if ! command -v docker &> /dev/null; then
        log_error "Docker is not installed. Please install Docker first."
        exit 1
    fi

    # Check if Docker buildx is available
    if ! docker buildx version &> /dev/null; then
        log_error "Docker buildx is not available. Please update Docker."
        exit 1
    fi

    # Check if logged in to GitHub Container Registry
    if ! docker login ghcr.io --get-login &>/dev/null; then
        log_warning "Not logged in to ghcr.io. Attempting login..."
        log_info "Use a GitHub PAT with 'write:packages' scope as the password"
        docker login ghcr.io
    fi

    log_success "Prerequisites check passed"
}

# Setup buildx builder
setup_buildx() {
    log_info "Setting up Docker buildx builder..."

    BUILDER_NAME="sonarr-builder"

    # Check if builder exists
    if docker buildx ls | grep -q "$BUILDER_NAME"; then
        log_info "Builder $BUILDER_NAME already exists, using it"
    else
        log_info "Creating new buildx builder: $BUILDER_NAME"
        docker buildx create --name "$BUILDER_NAME" --driver docker-container --use
        docker buildx inspect --bootstrap
    fi

    docker buildx use "$BUILDER_NAME"
    log_success "Buildx builder ready"
}

# Build and push sonarr image
build_sonarr() {
    local VERSION_TAG=$1
    local TAGS=""

    # Always include latest tag
    TAGS="--tag ${DOCKER_REGISTRY}/${IMAGE_NAME}:latest"

    # Add version tag if provided
    if [ -n "$VERSION_TAG" ] && [ "$VERSION_TAG" != "latest" ]; then
        TAGS="$TAGS --tag ${DOCKER_REGISTRY}/${IMAGE_NAME}:${VERSION_TAG}"
        log_version "Building with version tag: $VERSION_TAG"
    fi

    log_info "Building Sonarr image"
    log_info "Platforms: $PLATFORMS"
    log_info "Tags: latest${VERSION_TAG:+, $VERSION_TAG}"

    local CACHE_REPO="${DOCKER_REGISTRY}/${IMAGE_NAME}"

    docker buildx build \
        --platform "$PLATFORMS" \
        --file "$PROJECT_ROOT/Dockerfile" \
        $TAGS \
        --cache-from "type=registry,ref=${CACHE_REPO}:buildcache" \
        --cache-to "type=registry,ref=${CACHE_REPO}:buildcache,mode=max" \
        --push \
        --progress plain \
        "$PROJECT_ROOT"

    log_success "Sonarr image built and pushed"
}

# Main execution
main() {
    echo "================================================"
    echo "🚀 Sonarr Multi-Architecture Docker Build & Push"
    echo "================================================"
    echo

    # Parse arguments
    VERSION_TAG=${1:-5.0.0}

    # Check prerequisites
    check_prerequisites

    # Setup buildx
    setup_buildx

    # Build and push
    build_sonarr "$VERSION_TAG"

    echo
    echo "================================================"
    log_success "Build and push completed successfully!"
    echo "================================================"
    echo
    echo "Images pushed to ghcr.io:"
    echo "  - ${DOCKER_REGISTRY}/${IMAGE_NAME}:latest"
    if [ -n "$VERSION_TAG" ] && [ "$VERSION_TAG" != "latest" ]; then
        echo "  - ${DOCKER_REGISTRY}/${IMAGE_NAME}:${VERSION_TAG}"
    fi
    echo
    echo "Pull command:"
    echo "  docker pull ${DOCKER_REGISTRY}/${IMAGE_NAME}:latest"
    echo
    echo "Usage:"
    echo "  $0              # Build with 'latest' tag only"
    echo "  $0 1.0.0        # Build with '1.0.0' + latest tags"
    echo
}

# Run main function
main "$@"
