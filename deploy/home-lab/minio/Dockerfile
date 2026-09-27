# Builds MinIO from source (github.com/minio/minio). Docker Hub / quay.io no longer
# distribute pre-built community-edition images for anonymous pulls, so the server binary
# is compiled here instead of the docker-compose.yml referencing minio/minio:latest directly.
FROM golang:1.24-alpine AS build

RUN apk add --no-cache ca-certificates git

ENV CGO_ENABLED=0

RUN go install -trimpath -tags kqueue github.com/minio/minio@latest

FROM alpine:3.20

RUN apk add --no-cache ca-certificates

COPY --from=build /go/bin/minio /usr/bin/minio

VOLUME ["/data"]

ENTRYPOINT ["minio"]
CMD ["server", "/data"]
