// Package web embeds the static browser UI into the binary.
package web

import "embed"

//go:embed *.html *.css *.js
var FS embed.FS
