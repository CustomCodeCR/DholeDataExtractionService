variable "IMAGE_TAG" {
  default = "latest"
}

variable "SHA_TAG" {
  default = "local"
}

variable "GITHUB_ACTOR" {
  default = "github-actions"
}

group "default" {
  targets = ["dataextraction-api", "dataextraction-workers"]
}

target "common" {
  context    = "."
  dockerfile = ".github/docker/Dockerfile"
  args = {
    GITHUB_ACTOR = GITHUB_ACTOR
  }
  secret = ["id=github_token,env=GITHUB_TOKEN"]
  output = ["type=docker"]
}

target "dataextraction-api" {
  inherits = ["common"]
  target   = "api"
  tags = [
    "dhole/dataextraction-api:${IMAGE_TAG}",
    "dhole/dataextraction-api:${SHA_TAG}"
  ]
}

target "dataextraction-workers" {
  inherits = ["common"]
  target   = "workers"
  tags = [
    "dhole/dataextraction-workers:${IMAGE_TAG}",
    "dhole/dataextraction-workers:${SHA_TAG}"
  ]
}
