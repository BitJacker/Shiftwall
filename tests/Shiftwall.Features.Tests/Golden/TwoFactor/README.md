# Email 2FA golden files

Expected scripts for the email two-factor module, reviewed by hand. The configurations they come from are
generated in code by `Toolbox/TwoFactor/TwoFactorFixtures.cs` (synthetic: invented names, `example.com`, the
documentation address range 192.0.2.0/24 and the synthetic ENC test value).

- `one-user.txt`: one local user, batch and rollback.
- `fifty-users.txt`: 49 users in batches of 20, with the optional `config system global` timers.
- `multi-vdom.txt`: users in two VDOMs and one administrator with a FortiToken (kept), multi-VDOM wrapping.

When a golden file is missing the test writes `<name>.actual.txt` in the test output folder and fails.
