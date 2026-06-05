using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// ---------------------------------------------------------------------------
// METADADOS DE PUBLICADOR
// Estes campos aparecem nas propriedades do .exe e no dialogo do UAC.
// O publicador "SML" deve bater com o titular do certificado Authenticode usado
// para assinar (ver README -> "Como assinar" e o script sign.ps1).
// ---------------------------------------------------------------------------
[assembly: AssemblyTitle("SML Defender Control")]
[assembly: AssemblyDescription("Painel simples para controlar o Microsoft Defender (antivirus) e o Firewall do Windows.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("SML")]
[assembly: AssemblyProduct("SML Defender Control")]
[assembly: AssemblyCopyright("Copyright (c) 2026 SML. Licenca MIT.")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// FileDescription (mostrado pelo UAC e pelo Windows) = AssemblyTitle acima.
[assembly: NeutralResourcesLanguage("pt-BR")]

[assembly: ComVisible(false)]
[assembly: Guid("b7a1f4c2-1e3d-4a6b-9c8e-0d2f5a7b9c11")]

// Versao do produto e do arquivo.
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
