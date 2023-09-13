resource "aws_iam_policy" "ecs_task_execute" {
  policy = <<POLICY
{
   "Version": "2012-10-17",
   "Statement": [
       {
       "Effect": "Allow",
       "Action": [
            "ssmmessages:CreateControlChannel",
            "ssmmessages:CreateDataChannel",
            "ssmmessages:OpenControlChannel",
            "ssmmessages:OpenDataChannel"
       ],
      "Resource": "*"
      }
   ]
}
POLICY
}

resource "aws_iam_role" "ecs_task_execute" {
  name = join("-", [var.namespace, var.name, var.runtime, "task", "execute"])
  assume_role_policy = <<POLICY
{
   "Version":"2012-10-17",
   "Statement": [
      {
         "Effect":"Allow",
         "Principal":{
            "Service": [
               "ecs-tasks.amazonaws.com"
            ]
         },
         "Action":"sts:AssumeRole"
      }
   ]
}
POLICY
}

resource "aws_iam_role_policy_attachment" "ecs_task_execute" {
  role = aws_iam_role.ecs_task_execute.name
  policy_arn = aws_iam_policy.ecs_task_execute.arn
}

resource "aws_iam_policy" "ecs_task" {
  policy = <<POLICY
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Action": [
        "ec2:*",
        "ecs:*",
        "ecr:*",
        "ssm:*",
        "autoscaling:*",
        "elasticloadbalancing:*",
        "application-autoscaling:*",
        "logs:*",
        "tag:*",
        "resource-groups:*"
      ],
      "Effect": "Allow",
      "Resource": "*"
    }
  ]
}
POLICY
}

resource "aws_iam_role" "ecs_task" {
  name = join("-", [var.namespace, var.name, var.runtime, "task"])
  assume_role_policy = <<POLICY
{
   "Version":"2012-10-17",
   "Statement": [
      {
         "Effect":"Allow",
         "Principal":{
            "Service": [
               "ecs-tasks.amazonaws.com"
            ]
         },
         "Action":"sts:AssumeRole"
      }
   ]
}
POLICY
}

resource "aws_iam_role_policy_attachment" "ecs_task" {
  role = aws_iam_role.ecs_task.name
  policy_arn = aws_iam_policy.ecs_task.arn
}

resource "aws_ecs_task_definition" "obsidian_cloud_services" {
  family = "obsidian_cloud_services"
  task_role_arn = aws_iam_role.ecs_task.arn
  execution_role_arn = aws_iam_role.ecs_task_execute.arn
  network_mode = "awsvpc"
  cpu = 1024
  memory = 5120
  runtime_platform {
    cpu_architecture = "X86_64"
    operating_system_family = "LINUX"
  }
  container_definitions = jsonencode([
    {
      name      = "obsidianclient"
      image     = module.ecr.repository_url_map["obsidianclient"]
      cpu       = 10
      memory    = 512
      essential = true
      portMappings = [
        {
          containerPort = 25565
          hostPort      = 25565
        }
      ]
    },
    {
      name      = "obsidianworld"
      image     = module.ecr.repository_url_map["obsidianworld"]
      cpu       = 10
      memory    = 256
      essential = true
      portMappings = [
        {
          containerPort = 80
          hostPort      = 80
        }
      ]
    }
  ])
}

resource "aws_service_discovery_http_namespace" "obsidian_cloud" {
  name = join("-", [var.namespace, var.name, var.runtime])
}

resource "aws_ecs_cluster" "obsidian_cloud" {
  name = join("-", [var.namespace, var.name, var.runtime])
  service_connect_defaults {
    namespace = aws_service_discovery_http_namespace.obsidian_cloud.arn
  }
}

resource "aws_ecs_cluster_capacity_providers" "obsidian_cloud" {
  cluster_name = aws_ecs_cluster.obsidian_cloud.name

  capacity_providers = ["FARGATE"]

  default_capacity_provider_strategy {
    base              = 1
    weight            = 100
    capacity_provider = "FARGATE"
  }
}

resource "aws_lb_target_group" "obsidian_cloud" {
  name        = join("-", [var.namespace, var.name, var.runtime])
  port        = var.minecraft_port
  protocol    = "TCP"
  target_type = "ip"
  vpc_id      = module.vpc.vpc_id
}

resource "aws_lb" "obsidian_cloud" {
  name               = join("-", [var.namespace, var.name, var.runtime])
  internal           = false
  load_balancer_type = "network"
  subnets            = module.dynamic_subnets.public_subnet_ids

  enable_deletion_protection = false

  tags = {
    Environment = "production"
  }
}

resource "aws_lb_listener" "obsidian_cloud" {
  load_balancer_arn = aws_lb.obsidian_cloud.arn
  port              = "25565"
  protocol          = "TCP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.obsidian_cloud.arn
  }
}

resource "aws_security_group" "allow_minecraft" {
  name        = join("-", [var.namespace, var.name, var.runtime, "minecraft"])
  description = "Allow inbound minecraft traffic"
  vpc_id      = module.vpc.vpc_id
}

resource "aws_security_group_rule" "allow_minecraft" {
  type = "ingress"
  from_port = var.minecraft_port
  to_port = var.minecraft_port
  protocol = "tcp"
  cidr_blocks = ["0.0.0.0/0"]
  security_group_id  = aws_security_group.allow_minecraft.id
}

resource "aws_ecs_service" "obsidian_cloud" {
  name            = join("-", [var.namespace, var.name, var.runtime, "client"])
  cluster         = aws_ecs_cluster.obsidian_cloud.id
  task_definition = aws_ecs_task_definition.obsidian_cloud_services.arn
  desired_count   = 1

  network_configuration {
    subnets = module.dynamic_subnets.public_subnet_ids
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.obsidian_cloud.arn
    container_name   = "obsidianclient"
    container_port   = var.minecraft_port
  }

  # service_connect_configuration {
  #   enabled = true
  #   namespace = aws_service_discovery_http_namespace.obsidian_cloud.arn
  # }

}

# module "ecs" {
#   source = "terraform-aws-modules/ecs/aws"

#   cluster_name = var.namespace + "-" + var.name + "-" + var.runtime

#   cluster_configuration = {
#     execute_command_configuration = {
#       logging = "OVERRIDE"
#       log_configuration = {
#         cloud_watch_log_group_name = "/aws/ecs/aws-ec2"
#       }
#     }
#   }

#   fargate_capacity_providers = {
#     FARGATE = {
#       default_capacity_provider_strategy = {
#         weight = 100
#       }
#     }
#     FARGATE_SPOT = {
#       default_capacity_provider_strategy = {
#         weight = 0
#       }
#     }
#   }

#   services = {
#     obsidian = {
#       cpu    = 1024
#       memory = 4096

#       # Container definition(s)
#       container_definitions = {
#         obsidian-client = {
#           cpu = 512
#           memory = 1024
#           essential = true
#           image = "396454093479.dkr.ecr.us-east-1.amazonaws.com/obsidianclient:latest"
#           port_mappings = [
#             {
#               name = "minecraft-client"
#               containerPort = 80
#               protocol = "tcp"
#             }
#           ]
#           readonly_root_filesystem = false
#           enable_cloudwatch_logging = false
#         }

#         fluent-bit = {
#           cpu       = 512
#           memory    = 1024
#           essential = true
#           image     = "906394416424.dkr.ecr.us-west-2.amazonaws.com/aws-for-fluent-bit:stable"
#           firelens_configuration = {
#             type = "fluentbit"
#           }
#           memory_reservation = 50
#         }
#       }

#       service_connect_configuration = {
#         namespace = "obsidian"
#         service = {
#           client_alias = {
#             port     = 80
#             dns_name = "minecraft-client-handler"
#           }
#           port_name      = "minecraft-port"
#           discovery_name = "ecs-sample"
#         }
#       }

#       load_balancer = {
#         service = {
#           target_group_arn = "arn:aws:elasticloadbalancing:eu-west-1:1234567890:targetgroup/bluegreentarget1/209a844cd01825a4"
#           container_name   = "ecs-sample"
#           container_port   = 80
#         }
#       }

#       subnet_ids = module.dynamic_subnets.subnet_ids
#       security_group_rules = {
#         alb_ingress_3000 = {
#           type                     = "ingress"
#           from_port                = 80
#           to_port                  = 80
#           protocol                 = "tcp"
#           description              = "Service port"
#           source_security_group_id = "sg-12345678"
#         }
#         egress_all = {
#           type        = "egress"
#           from_port   = 0
#           to_port     = 0
#           protocol    = "-1"
#           cidr_blocks = ["0.0.0.0/0"]
#         }
#       }
#     }
#   }

#   tags = {
#     Environment = "Development"
#     Project     = "Example"
#   }
# }