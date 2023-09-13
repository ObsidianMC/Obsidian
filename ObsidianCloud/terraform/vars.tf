variable "namespace" {
  description = "Prefix for all resources. Ex: oc"
  type        = string
}

variable "name" {
  description = "Unique name for this instance of Obsidian Cloud."
  type        = string
}

variable "runtime" {
  description = "Ex: dev|qa|prod"
  type        = string
}

variable "minecraft_port" {
  description = "Port for the minecraft client to connect to. Ex: 25565"
  type        = number
  default     = 25565
}