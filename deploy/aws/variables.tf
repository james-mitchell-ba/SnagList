variable "aws_region" {
  type = string
}
variable "name_prefix" {
  type    = string
  default = "snaglist"
}
variable "availability_zones" {
  type = list(string)
}
variable "deletion_protection" {
  type    = bool
  default = true
}
variable "github_repository" {
  type = string
}
variable "image_tag" {
  type = string
}
variable "email_domain_name" {
  type = string
}
variable "email_from_address" {
  type = string
}
variable "maintenance_team_email" {
  type = string
}
variable "entra_tenant_id" {
  type = string
}
variable "entra_audience" {
  type    = string
  default = "snaglist-api"
}
