# Builds the MinIO client (github.com/minio/mc) from source for the same reason
# as ./Dockerfile: no pre-built image is published for anonymous pulls anymore.
FROM golang:1.24-alpine AS build

RUN apk add --no-cache ca-certificates git

ENV CGO_ENABLED=0

RUN go install -trimpath github.com/minio/mc@latest

FROM alpine:3.20

RUN apk add --no-cache ca-certificates

COPY --from=build /go/bin/mc /usr/bin/mc

ENTRYPOINT ["mc"]
