module "vpc" {
  source                           = "cloudposse/vpc/aws"
  namespace                        = var.namespace
  stage                            = var.runtime
  name                             = var.name
  ipv4_primary_cidr_block          = "10.0.0.0/16"
  assign_generated_ipv6_cidr_block = false
}

module "dynamic_subnets" {
  source           = "cloudposse/dynamic-subnets/aws"
  namespace        = var.namespace
  stage            = var.runtime
  name             = var.name
  vpc_id           = module.vpc.vpc_id
  igw_id           = [module.vpc.igw_id]
  ipv4_cidr_block  = ["10.0.0.0/16"]
  max_subnet_count = 3
}


