output "dkim_tokens" {
  value = aws_sesv2_email_identity.domain.dkim_signing_attributes[0].tokens
}

output "lambda_ses_send_policy_json" {
  value = data.aws_iam_policy_document.lambda_ses_send.json
}
