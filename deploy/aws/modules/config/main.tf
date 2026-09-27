resource "aws_ssm_parameter" "storage_bucket_name" {
  name  = "/${var.name_prefix}/Storage/BucketName"
  type  = "String"
  value = var.storage_bucket_name
}

resource "aws_ssm_parameter" "notifications_maintenance_email" {
  name  = "/${var.name_prefix}/Notifications/MaintenanceTeamEmail"
  type  = "String"
  value = var.maintenance_team_email
}

resource "aws_ssm_parameter" "auth_entraid_tenant_id" {
  name  = "/${var.name_prefix}/Auth/EntraId/TenantId"
  type  = "String"
  value = var.entra_tenant_id
}

resource "aws_ssm_parameter" "auth_entraid_audience" {
  name  = "/${var.name_prefix}/Auth/EntraId/Audience"
  type  = "String"
  value = var.entra_audience
}

data "aws_iam_policy_document" "lambda_config_access" {
  statement {
    sid       = "ReadDbAppSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.db_app_secret_arn]
  }
  statement {
    sid     = "ReadSsmConfig"
    actions = ["ssm:GetParameter", "ssm:GetParameters"]
    resources = [
      aws_ssm_parameter.storage_bucket_name.arn,
      aws_ssm_parameter.notifications_maintenance_email.arn,
      aws_ssm_parameter.auth_entraid_tenant_id.arn,
      aws_ssm_parameter.auth_entraid_audience.arn,
    ]
  }
}
