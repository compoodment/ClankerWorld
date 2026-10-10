'use strict';
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const assert = require('node:assert/strict');
const workflow = fs.readFileSync(path.join(__dirname, '../workflows/windows-checkpoint-trace.yml'), 'utf8');

test('checkpoint tracing is manual and passes dispatch choices through validated environment values', () => {
  const trigger = workflow.slice(workflow.indexOf('\non:'), workflow.indexOf('\npermissions:'));
  assert.match(trigger, /workflow_dispatch:/);
  assert.doesNotMatch(trigger, /(?:push|pull_request|schedule):/);
  assert.match(workflow, /contents: read/);
  assert.match(workflow, /TRACE_MODE: \$\{\{ inputs\.mode \}\}/);
  assert.match(workflow, /TRACE_ITERATIONS: \$\{\{ inputs\.iterations \}\}/);
  const run = workflow.split('\n').find(line => line.includes('run: ./scripts/trace-windows-checkpoints.ps1'));
  assert.ok(run);
  assert.doesNotMatch(run, /\$\{\{/);
  assert.match(run, /-Mode \$env:TRACE_MODE -Iterations \(\[int\]\$env:TRACE_ITERATIONS\)/);
});

test('checkpoint trace uploads synthetic exports and never the raw whole-machine trace', () => {
  const upload = workflow.slice(workflow.indexOf('      - name: Upload only synthetic checkpoint evidence'));
  assert.match(upload, /if: \$\{\{ !cancelled\(\) \}\}/);
  assert.match(upload, /checkpoint-trace\/\*-checkpoint-events\.jsonl/);
  assert.match(upload, /checkpoint-trace\/checkpoint-evidence/);
  assert.match(upload, /checkpoint-trace\/results/);
  const paths = upload.slice(upload.indexOf('          path:'), upload.indexOf('          include-hidden-files:'));
  assert.doesNotMatch(paths, /checkpoint-trace\/raw|checkpoint-trace\s*\n|\.etl|\.xml/);
  assert.match(upload, /if-no-files-found: error/);
});
