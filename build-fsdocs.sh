#!/bin/bash
dotnet tool restore
dotnet build src

dotnet fsdocs build \
  --nodefaultcontent \
  --output ./docs \
  --input ./fsdocs \
  --parameters root "$1"
