terraform {
  backend "s3" {
    bucket = "obsidian-terraform"
    key    = "obsidian-cloud.tfstate"
    region = "us-east-1"
  }
}

provider "aws" {
  region = "us-east-1"
}