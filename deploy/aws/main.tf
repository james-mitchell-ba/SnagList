terraform {
  required_version = ">= 1.9"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.0" }
  }
  # bucket/key/region/dynamodb_table supplied via `-backend-config` at init time, from the
  # bootstrap module's own outputs (Task 4) — hardcoding them here would recreate the exact
  # chicken-and-egg problem the bootstrap module exists to avoid.
  backend "s3" {}
}

provider "aws" {
  region = var.aws_region
}

module "networking" {
  source             = "./modules/networking"
  name_prefix        = var.name_prefix
  availability_zones = var.availability_zones
}

module "database" {
  source                      = "./modules/database"
  name_prefix                 = var.name_prefix
  private_subnet_ids          = module.networking.private_subnet_ids
  database_security_group_id  = module.networking.database_security_group_id
  deletion_protection         = var.deletion_protection
}

resource "aws_s3_bucket" "photos" {
  bucket = "${var.name_prefix}-photos"
}

resource "aws_s3_bucket_server_side_encryption_configuration" "photos" {
  bucket = aws_s3_bucket.photos.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}

resource "aws_s3_bucket_public_access_block" "photos" {
  bucket                  = aws_s3_bucket.photos.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

module "email" {
  source      = "./modules/email"
  domain_name = var.email_domain_name
}

module "config" {
  source                 = "./modules/config"
  name_prefix            = var.name_prefix
  storage_bucket_name    = aws_s3_bucket.photos.bucket
  maintenance_team_email = var.maintenance_team_email
  entra_tenant_id        = var.entra_tenant_id
  entra_audience         = var.entra_audience
  db_app_secret_arn      = module.database.app_secret_arn
}

module "compute" {
  source                            = "./modules/compute"
  name_prefix                       = var.name_prefix
  github_repository                 = var.github_repository
  image_tag                         = var.image_tag
  private_subnet_ids                = module.networking.private_subnet_ids
  lambda_security_group_id          = module.networking.lambda_security_group_id
  db_app_secret_arn                 = module.database.app_secret_arn
  db_address                        = module.database.address
  db_name                           = module.database.db_name
  photos_bucket_arn                 = aws_s3_bucket.photos.arn
  photos_bucket_name                = aws_s3_bucket.photos.bucket
  email_from_address                = var.email_from_address
  maintenance_team_email            = var.maintenance_team_email
  entra_tenant_id                   = var.entra_tenant_id
  entra_audience                    = var.entra_audience
  lambda_config_access_policy_json  = module.config.lambda_config_access_policy_json
  lambda_ses_send_policy_json       = module.email.lambda_ses_send_policy_json
}

module "static_site" {
  source      = "./modules/static-site"
  name_prefix = var.name_prefix
}
