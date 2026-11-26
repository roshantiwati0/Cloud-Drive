# Render harness

Draws PC windows and pages with a real WPF dispatcher and saves them as PNG files, so they can be checked
without clicking through the app.

    dotnet build -c Debug -o bin
    dotnet bin\Render.dll <out folder> <page>

Pages include devices, deletes, guest, bell, hub, send, sendfolder, perm, browse, add and remote.
`screenshots/` holds earlier results.
