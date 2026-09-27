resource "aws_sesv2_email_identity" "domain" {
  email_identity = var.domain_name
}

data "aws_iam_policy_document" "lambda_ses_send" {
  statement {
    sid       = "SendEmailFromVerifiedDomain"
    actions   = ["ses:SendEmail"]
    resources = [aws_sesv2_email_identity.domain.arn]
  }
}
