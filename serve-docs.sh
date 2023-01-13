#!/bin/bash

./build-fsdocs.sh "http://localhost:8089/fsc-host/"
docker run -p 8089:8089 --rm -it -v ~/.ssh:/root/.ssh -v "${PWD}:/docs" squidfunk/mkdocs-material serve -a 0.0.0.0:8089
./build-fsdocs.sh "https://queil.github.io/fsc-host/"
