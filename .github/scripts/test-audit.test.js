const assert = require('node:assert/strict');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

test('test-audit helpers preserve trustworthy coverage evidence', () => {
  const root = path.join(__dirname, '..', '..');
  const python = process.env.PYTHON || (process.platform === 'win32' ? 'python' : 'python3');
  const result = spawnSync(python, ['-m', 'unittest', 'discover', '-s', 'skills/test-audit/scripts', '-p', 'test_*.py'], {
    cwd: root, encoding: 'utf8', timeout: 20000,
    env: { ...process.env, PYTHONDONTWRITEBYTECODE: '1' },
  });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
});
