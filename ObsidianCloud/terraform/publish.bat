pushd ..\..\

docker build -f ObsidianCloud\ObsidianCloud.AWS.ClientService\Dockerfile -t %AWS_ACCOUNT%.dkr.ecr.%AWS_REGION%.amazonaws.com/obsidianclient:latest .
docker build -f ObsidianCloud\ObsidianCloud.AWS.ClientService\Dockerfile -t %AWS_ACCOUNT%.dkr.ecr.%AWS_REGION%.amazonaws.com/obsidianworld:latest .

@echo off
if errorlevel 1 (
    popd
    echo Build failed!
    exit /b %errorlevel%
)

for /f %%i in ('aws ecr get-login-password --region %AWS_REGION%') do (
    docker login --username AWS --password %%i %AWS_ACCOUNT%.dkr.ecr.%AWS_REGION%.amazonaws.com
)
@echo on
docker push %AWS_ACCOUNT%.dkr.ecr.%AWS_REGION%.amazonaws.com/obsidianclient:latest
docker push %AWS_ACCOUNT%.dkr.ecr.%AWS_REGION%.amazonaws.com/obsidianworld:latest

popd