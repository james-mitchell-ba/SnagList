output "lambda_config_access_policy_json" {
  value = data.aws_iam_policy_document.lambda_config_access.json
}

output "ssm_parameter_names" {
  value = {
    storage_bucket_name              = aws_ssm_parameter.storage_bucket_name.name
    notifications_maintenance_email  = aws_ssm_parameter.notifications_maintenance_email.name
    auth_entraid_tenant_id           = aws_ssm_parameter.auth_entraid_tenant_id.name
    auth_entraid_audience            = aws_ssm_parameter.auth_entraid_audience.name
  }
}
