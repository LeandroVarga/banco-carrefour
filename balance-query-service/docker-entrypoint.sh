#!/bin/sh
set -eu
[ -f /run/secrets/API_KEY ] && export API_KEY="$(cat /run/secrets/API_KEY)"
[ -f /run/secrets/SPRING_DATASOURCE_USERNAME ] && export SPRING_DATASOURCE_USERNAME="$(cat /run/secrets/SPRING_DATASOURCE_USERNAME)"
[ -f /run/secrets/SPRING_DATASOURCE_PASSWORD ] && export SPRING_DATASOURCE_PASSWORD="$(cat /run/secrets/SPRING_DATASOURCE_PASSWORD)"
exec java -jar /app/app.jar

