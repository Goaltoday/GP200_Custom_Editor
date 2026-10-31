# Publicar en GitHub

La carpeta contiene el repositorio preparado, sin SDK, firmware, manuales, registros privados ni gráficos originales o con marcas. No existe todavía un repositorio remoto. Para muchos archivos de dependencias conviene usar GitHub Desktop o Git.

## Con GitHub Desktop

1. Añade esta carpeta como repositorio local. Si Desktop indica que aún no es un repositorio Git, utiliza la opción de crearlo en esta misma carpeta, conservando sus archivos.
2. Revisa los cambios y realiza el primer commit.
3. Pulsa Publish repository con tu cuenta Goaltoday. Escoge el nombre y desmarca la opción privada si quieres que sea público.
4. Comprueba en GitHub que aparecen README, LICENSE GPL-3.0, Editor, Native, scripts y .github.
5. El workflow compila la aplicación autocontenida para Windows x64 y sube la carpeta con el `.exe`, runtime .NET y recursos como artefacto. Descárgalo en **Actions → ejecución completada → Artifacts** y extrae su contenido en una carpeta. El usuario no necesita instalar .NET. No crea un Release y no comunica con la GP200.

No publiques el handoff interno FUENTES ni el SDK.

## Con Git

Abre PowerShell dentro de esta carpeta:

```powershell
git init -b main
git add .
git commit -m "Initial GPL-3.0 release"
```

Crea un repositorio público vacío en tu cuenta, sin generar otro README ni otra licencia. Copia su URL y ejecuta:

```powershell
git remote add origin URL_DEL_REPOSITORIO
git push -u origin main
```

## Publicar el ejecutable

El workflow no crea un Release ni un ZIP. Descarga la carpeta autocontenida desde el artefacto de la ejecución de Actions.

Los gráficos incluidos y sus licencias se describen en [ASSETS.md](ASSETS.md).
