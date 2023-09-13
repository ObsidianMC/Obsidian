module "ecr" {
  source                 = "cloudposse/ecr/aws"
  namespace              = var.namespace
  stage                  = var.runtime
  name                   = var.name
  image_names = [
    "obsidianclient",
    "obsidianworld",
    "obsidiangenerator"
  ]
  principals_full_access = []
  principals_readonly_access = []
  principals_lambda = []
}