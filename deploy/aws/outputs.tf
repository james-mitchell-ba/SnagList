output "api_invoke_url" {
  value = module.compute.api_invoke_url
}
output "mcp_invoke_url" {
  value = module.compute.mcp_invoke_url
}
output "web_bucket_name" {
  value = module.static_site.bucket_name
}
output "web_cloudfront_distribution_id" {
  value = module.static_site.cloudfront_distribution_id
}
output "web_cloudfront_domain_name" {
  value = module.static_site.cloudfront_domain_name
}
output "github_actions_deploy_role_arn" {
  value = module.compute.github_actions_deploy_role_arn
}
output "ses_dkim_tokens" {
  value = module.email.dkim_tokens
}
