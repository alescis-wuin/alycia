FROM postgres:17-alpine AS build
RUN apk add --no-cache build-base curl
WORKDIR /tmp/pgvector
RUN curl -fsSL https://github.com/pgvector/pgvector/archive/refs/tags/v0.8.7.tar.gz -o source.tar.gz \
    && echo 'cac0b10c360f05b2d521200105ba3697e773d4cd3731f5a915a7e37ebe0bea85  source.tar.gz' | sha256sum -c - \
    && tar -xzf source.tar.gz --strip-components=1 \
    && make with_llvm=no OPTFLAGS='' \
    && make with_llvm=no install

FROM postgres:17-alpine
COPY --from=build /usr/local/lib/postgresql/vector.so /usr/local/lib/postgresql/vector.so
COPY --from=build /usr/local/share/postgresql/extension/vector* /usr/local/share/postgresql/extension/
