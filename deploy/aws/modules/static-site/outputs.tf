output "bucket_name" {
  value = aws_s3_bucket.web.bucket
}
output "cloudfront_distribution_id" {
  value = aws_cloudfront_distribution.web.id
}
output "cloudfront_domain_name" {
  value = aws_cloudfront_distribution.web.domain_name
}
