#r "nuget: Yzl, 2.0.0"

open Yzl

let trees = Yzl.seq

trees [
  "oak"
  "pine"
  "spruce"
] 
|> Yzl.render 
|> printf "%s"
