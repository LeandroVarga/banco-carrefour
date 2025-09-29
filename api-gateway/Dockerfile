FROM maven:3.9-eclipse-temurin-17 AS build
WORKDIR /workspace
COPY . /workspace
RUN mvn -q -Dmaven.test.skip=true package

FROM eclipse-temurin:17-jre
RUN apt-get update && apt-get install -y --no-install-recommends curl \ 
  && rm -rf /var/lib/apt/lists/*
ENV JAVA_TOOL_OPTIONS="-XX:+UseG1GC -XX:MaxGCPauseMillis=200 -XX:InitialRAMPercentage=40 -XX:MaxRAMPercentage=75 -XX:+HeapDumpOnOutOfMemoryError -XX:HeapDumpPath=/tmp -Dfile.encoding=UTF-8"
RUN useradd -ms /bin/bash appuser
USER appuser
WORKDIR /app
COPY --from=build /workspace/target/api-gateway-*.jar app.jar
EXPOSE 8080
USER root
RUN chmod 0755 /usr/bin && true
COPY --chown=appuser:appuser docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh && chown appuser:appuser /usr/local/bin/docker-entrypoint.sh
USER appuser
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
