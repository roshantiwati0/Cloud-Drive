# The app's icon files are made from one drawing now (tools\icons): this script only runs those steps in order.
#   node tools\icons\make-icons.js     the drawing as Android vectors and as SVG (phone app, web page)
#   node tools\icons\png.js            PNG versions, and the PC app's picture Assets\clouddrive.png
#   tools\icons\make-ico.ps1           Assets\clouddrive.ico and Assets\cloudssd.ico from that picture
# The pictures the app had before 14 Nov 2026 (this script drew one of them) are kept in backup\photo.
$tools = Join-Path $PSScriptRoot '..\tools\icons'
node (Join-Path $tools 'make-icons.js')
node (Join-Path $tools 'png.js')
& (Join-Path $tools 'make-ico.ps1')
