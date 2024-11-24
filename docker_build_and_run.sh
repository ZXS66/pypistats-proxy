docker build -t pypistats-proxy:0.0.1 .
# stop and remove existing pod
docker stop pypistats-proxy-pod
docker rm pypistats-proxy-pod
docker run -itd --restart=always -p 8080:8080 --name pypistats-proxy-pod pypistats-proxy:0.0.1
docker ps
