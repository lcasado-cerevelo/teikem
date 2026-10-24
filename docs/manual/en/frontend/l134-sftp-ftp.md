# L134 — SFTP and FTP integrations (staff web)

Screenshots from the `web-app/e2e/lote134-sftp-ftp.spec.ts` walkthrough against the real API and test SFTP/FTP servers (desktop and mobile at 360 px with no horizontal scroll). Rules and errors: [chapter 39, §7](../39-integraciones.md).

## List
The SFTP and FTP connectors with **Test connection**, **Run now**, **Upload file** and **History**.

![List](img/l134-lista.png)

## Form (SFTP connector)
Server, port, user, authentication (password or private key; the secrets are never shown again), remote folder, processed and errors, **pinned server fingerprint**, and the file filter by name and type. In FTP, instead of the fingerprint, the **FTPS** checkbox.

![Form](img/l134-formulario.png)
