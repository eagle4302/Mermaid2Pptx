. "$PSScriptRoot/TestSupport.ps1"

$missingThrowWasDetected = $false
try {
    Assert-Throws {
        "action completed without throwing" | Out-Null
    } "expected-pattern"
}
catch {
    $missingThrowWasDetected = $true
}

Assert-True $missingThrowWasDetected "Assert-Throws rejects actions that do not throw"

Assert-Throws {
    throw "expected-pattern from action"
} "expected-pattern"

$wrongErrorWasDetected = $false
try {
    Assert-Throws {
        throw "different error"
    } "expected-pattern"
}
catch {
    $wrongErrorWasDetected = $true
}

Assert-True $wrongErrorWasDetected "Assert-Throws rejects the wrong error"
