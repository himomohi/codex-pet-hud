#!/bin/bash
set -euo pipefail

DIR="${1:-dist}"
cd "$DIR"
shopt -s nullglob
archives=(Codex-Pet-HUD-v*.zip)
[[ ${#archives[@]} -eq 3 ]] || { echo "Expected exactly 3 release archives, found ${#archives[@]}" >&2; exit 1; }

for archive in "${archives[@]}"; do
  unzip -tqq "$archive"
  listing="$(unzip -Z1 "$archive")"
  if grep -Eiq '(^|/)(\.git|\.codex|auth\.json|\.env|logs?|bin|obj)(/|$)|\.(p12|cer|mobileprovision)$|/Users/' <<<"$listing"; then
    echo "Forbidden release content in $archive" >&2
    exit 1
  fi
  if [[ "$archive" == *-Windows-*.zip ]]; then
    for required in app/CodexPetLimitRings.exe Install.ps1 Uninstall.ps1 LICENSE.txt README.txt; do
      grep -Fxq "$required" <<<"$listing" || {
        echo "Missing $required in $archive" >&2
        exit 1
      }
    done
    installer="$(unzip -p "$archive" Install.ps1)"
    [[ "$(grep -Fc 'Unblock-File' <<<"$installer")" -eq 1 && "$(grep -Fc 'Unblock-File -LiteralPath $Executable' <<<"$installer")" -eq 1 ]] || {
      echo "Install.ps1 must unblock only the installed executable once in $archive" >&2
      exit 1
    }
    grep -Fq 'Remove-ItemProperty $RunKey -Name "CodexPetLimitRings"' <<<"$installer" || {
      echo "Install.ps1 does not remove the stale startup entry in $archive" >&2
      exit 1
    }
    grep -Fq 'WorkingDirectory = $InstallRoot' <<<"$installer" || {
      echo "Install.ps1 does not launch from the install directory in $archive" >&2
      exit 1
    }
    grep -Fq '$_.Exception.NativeErrorCode -eq 1223' <<<"$installer" || {
      echo "Install.ps1 does not handle Windows error 1223 in $archive" >&2
      exit 1
    }
    remove_line="$(grep -nF 'Remove-ItemProperty $RunKey -Name "CodexPetLimitRings"' <<<"$installer" | cut -d: -f1)"
    unblock_line="$(grep -nF 'Unblock-File -LiteralPath $Executable' <<<"$installer" | cut -d: -f1)"
    launch_line="$(grep -nF '$Process = [System.Diagnostics.Process]::Start($StartInfo)' <<<"$installer" | cut -d: -f1)"
    health_line="$(grep -nF 'if ($Process.HasExited)' <<<"$installer" | cut -d: -f1)"
    register_line="$(grep -nF 'New-ItemProperty $RunKey -Name "CodexPetLimitRings"' <<<"$installer" | cut -d: -f1)"
    [[ "$remove_line" =~ ^[0-9]+$ && "$unblock_line" =~ ^[0-9]+$ && "$launch_line" =~ ^[0-9]+$ && "$health_line" =~ ^[0-9]+$ && "$register_line" =~ ^[0-9]+$ ]] &&
      (( remove_line < unblock_line && unblock_line < launch_line && launch_line < health_line && health_line < register_line )) || {
        echo "Install.ps1 startup transaction is out of order in $archive" >&2
        exit 1
      }
  fi
done

sha256sum "${archives[@]}" > SHA256SUMS.txt
sha256sum -c SHA256SUMS.txt
