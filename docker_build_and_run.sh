docker build -t pypistats-proxy:0.0.1 .
docker run -d --restart=always -p 8080:8080 pypistats-proxy:0.0.1
docker ps