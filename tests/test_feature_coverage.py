"""Contract integrity tests; these do not accept native launcher functionality."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class CoverageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = ROOT / 'tools/validate_feature_coverage.py'
        if not path.is_file():
            raise AssertionError('Coverage validator has not been implemented')
        spec = importlib.util.spec_from_file_location('coverage_validator', path)
        assert spec and spec.loader
        cls.validator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.validator)
        cls.contract = json.loads((ROOT / 'docs/FEATURE_COVERAGE.json').read_text())
        cls.scenarios = json.loads((ROOT / 'tests/fixtures/scenarios.json').read_text())

    def check_bad(self, mutate, message):
        contract, scenarios = copy.deepcopy(self.contract), copy.deepcopy(self.scenarios)
        mutate(contract, scenarios)
        errors = self.validator.validate_contract(contract, scenarios)
        self.assertTrue(any(message in error for error in errors), errors)

    def test_contract_validates(self):
        self.assertEqual(self.validator.validate_contract(self.contract, self.scenarios), [])

    def test_cli_validates_real_files(self):
        result = subprocess.run([sys.executable, 'tools/validate_feature_coverage.py'], cwd=ROOT,
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('native acceptance is not implied', result.stdout)

    def test_cli_rejects_duplicate_json_keys_and_nonfinite_numbers(self):
        for invalid in [json.dumps(self.contract).replace('"schema_version": 1', '"schema_version": 99, "schema_version": 1', 1),
                        json.dumps(self.contract).replace('"scope":', '"scope": NaN, "ignored":', 1)]:
            with tempfile.TemporaryDirectory(prefix='relicade-coverage-json-') as temp:
                path = Path(temp) / 'contract.json'
                path.write_text(invalid)
                result = subprocess.run([sys.executable, 'tools/validate_feature_coverage.py', '--contract', str(path)],
                                        cwd=ROOT, capture_output=True, text=True)
                self.assertEqual(result.returncode, 1)
                self.assertIn('coverage validation failed:', result.stderr)

    def test_schema_version_and_unknown_fields(self):
        self.check_bad(lambda c, s: c.update(schema_version=99), 'schema_version')
        self.check_bad(lambda c, s: c.update(accepted=True), 'unknown field')

    def test_malformed_shapes_fail_closed(self):
        for value in [None, [], '', 7, True]:
            self.assertTrue(self.validator.validate_contract(value, self.scenarios))
        for field in ['features', 'platforms', 'categories', 'boundaries', 'evidence']:
            self.check_bad(lambda c, s, field=field: c.update({field: None}), field)
        self.check_bad(lambda c, s: c['features'].__setitem__(0, None), 'features')
        self.check_bad(lambda c, s: c['features'][0].update(targets=[]), 'targets')
        self.assertTrue(self.validator.validate_contract(self.contract, None))

    def test_duplicate_feature_platform_and_scenario_ids(self):
        self.check_bad(lambda c, s: c['features'].append(c['features'][0]), 'duplicate')
        self.check_bad(lambda c, s: c['platforms'].append(c['platforms'][0]), 'duplicate')
        self.check_bad(lambda c, s: s['scenarios'].append(s['scenarios'][0]), 'duplicate')

    def test_mandatory_platforms_categories_and_feature_ids(self):
        self.check_bad(lambda c, s: c['platforms'].pop(), 'mandatory platform')
        self.check_bad(lambda c, s: c['categories'].pop(), 'mandatory categor')
        self.check_bad(lambda c, s: c['features'].pop(0), 'mandatory feature')
        self.check_bad(lambda c, s: c['features'][0].update(category='invented'), 'category')

    def test_each_feature_has_every_platform_with_reason(self):
        self.check_bad(lambda c, s: c['features'][0]['targets'].pop('windows-x64'), 'target set')
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(reason=''), 'reason')

    def test_platform_and_feature_status_vocabularies(self):
        self.check_bad(lambda c, s: c['platforms'][0].update(implementation='done'), 'implementation')
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(validation='passed'), 'validation')
        self.check_bad(lambda c, s: c['features'][0].update(priority='optional'), 'priority')

    def test_missing_or_unknown_fixture_rejected(self):
        self.check_bad(lambda c, s: c['features'][0].update(fixtures=[]), 'fixtures')
        self.check_bad(lambda c, s: c['features'][0].update(fixtures=['nonexistent']), 'unknown fixture')

    def test_acceptance_without_evidence_rejected(self):
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(validation='accepted'), 'accepted requires')
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(implementation='supported'), 'supported requires')

    def test_not_applicable_requires_explicit_reason_and_no_acceptance(self):
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(applicability='not_applicable', validation='accepted'), 'not_applicable')
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(applicability='not_applicable', validation='not_run'), 'not_applicable')

    def test_desktop_and_android_boundaries(self):
        self.check_bad(lambda c, s: c['platforms'][-1].update(payload_kind='desktop-executable'), 'payload_kind')
        def accept_desktop_on_android(c, s):
            row = next(f for f in c['features'] if f['id'] == 'platform.desktop-execution')
            row['targets']['android-apk'].update(applicability='required', validation='not_run')
        self.check_bad(accept_desktop_on_android, 'desktop-execution')

    def test_evidence_references_must_exist(self):
        self.check_bad(lambda c, s: c['features'][0]['targets']['windows-x64'].update(evidence=['missing']), 'unknown evidence')

    def evidence_case(self):
        c = copy.deepcopy(self.contract)
        row = c['features'][0]
        fixture = row['fixtures'][0]
        # Deliberately artificial record used ONLY to test the validator, never published as evidence.
        base = dict(platform='windows-x64', commit='a' * 40, fixture=fixture,
                    command='runner --synthetic', result='pass', output='exit 0; assertions passed',
                    artifact='evidence/test-run.txt', hardware='isolated test device description',
                    feature=row['id'])
        # Preserve existing bounded source records and their references. Synthetic
        # acceptance records stay last so mutations target the intended runtime.
        c['evidence'] += [dict(base, id=kind.replace('_', '-'), kind=kind) for kind in ['unit', 'package', 'native_runtime']]
        row['targets']['windows-x64'].update(implementation='supported', validation='accepted',
                                              evidence=['unit', 'package', 'native-runtime'])
        return c

    def test_complete_record_shape_can_be_validated_without_claiming_execution(self):
        self.assertEqual(self.validator.validate_contract(self.evidence_case(), self.scenarios), [])

    def test_source_build_browser_or_definition_cannot_accept_native(self):
        for kind in ['source_inspection', 'source_build', 'browser', 'fixture_definition']:
            c = self.evidence_case()
            c['evidence'][-1]['kind'] = kind
            self.assertTrue(any('accepted requires' in e for e in self.validator.validate_contract(c, self.scenarios)))

    def test_incomplete_failed_wrong_platform_or_wrong_feature_evidence_rejected(self):
        for field, value in [('commit', 'HEAD'), ('command', ''), ('output', ''), ('artifact', '/private/log'),
                             ('hardware', ''), ('result', 'fail'), ('fixture', 'missing'),
                             ('platform', 'macos-arm64'), ('feature', 'catalog.detail')]:
            with self.subTest(field=field):
                c = self.evidence_case()
                c['evidence'][-1][field] = value
                self.assertTrue(self.validator.validate_contract(c, self.scenarios))

    def test_mixed_commit_acceptance_rejected(self):
        c = self.evidence_case()
        c['evidence'][-1]['commit'] = 'b' * 40
        self.assertTrue(any('same commit' in e for e in self.validator.validate_contract(c, self.scenarios)))

    def test_platform_support_requires_build_package_and_runtime_evidence(self):
        self.check_bad(lambda c, s: c['platforms'][0].update(implementation='supported'), 'supported requires')
        self.check_bad(lambda c, s: c['platforms'][0]['checks']['runtime'].update(status='accepted'), 'accepted requires')

    def test_mandatory_features_cannot_be_waived_as_not_applicable(self):
        def waive(c, s):
            c['features'][0]['targets']['windows-x64'].update(
                applicability='not_applicable', implementation='not_applicable', validation='not_applicable')
        self.check_bad(waive, 'mandatory applicability')

    def test_nested_malformed_objects_report_errors_not_crashes(self):
        self.check_bad(lambda c, s: c['features'][0]['targets'].update({'android-apk': None}), 'targets')
        self.check_bad(lambda c, s: c['platforms'][0]['checks'].update(runtime=[]), 'runtime')
        self.check_bad(lambda c, s: c['features'][0].update(requirement='made-up-spec'), 'requirement')
        self.check_bad(lambda c, s: s['scenarios'][0].update(artifacts=['../unsafe']), 'artifacts')

    def test_all_nested_json_shapes_fail_closed_without_exceptions(self):
        # Walk every field of representative objects, not random/fuzzy timing.
        paths = [(), ('platforms', 0), ('platforms', 0, 'checks', 'runtime'),
                 ('features', 0), ('features', 0, 'targets', 'windows-x64'),
                 ('boundaries',)]
        for path in paths:
            original = self.contract
            for part in path:
                original = original[part]
            for field in original:
                for invalid in [None, [], {}, 7, True, '']:
                    c = copy.deepcopy(self.contract)
                    target = c
                    for part in path:
                        target = target[part]
                    target[field] = invalid
                    with self.subTest(path=path, field=field, value=invalid):
                        self.assertIsInstance(self.validator.validate_contract(c, self.scenarios), list)
        c = self.evidence_case()
        for field in next(record for record in c['evidence'] if record['id'] == 'unit'):
            for invalid in [None, [], {}, 7, True, '']:
                modified = copy.deepcopy(c)
                next(record for record in modified['evidence'] if record['id'] == 'unit')[field] = invalid
                self.assertTrue(self.validator.validate_contract(modified, self.scenarios))

    def test_platform_build_package_runtime_checks_can_be_recorded_separately(self):
        c = self.evidence_case()
        build = dict(next(record for record in c['evidence'] if record['id'] == 'unit'), id='source-build', kind='source_build')
        c['evidence'].append(build)
        p = c['platforms'][0]
        p['implementation'] = 'supported'
        for key, eid in [('build', 'source-build'), ('package', 'package'), ('runtime', 'native-runtime')]:
            p['checks'][key].update(status='accepted', evidence=[eid])
        self.assertEqual(self.validator.validate_contract(c, self.scenarios), [])
        p['checks']['runtime']['evidence'] = ['source-build']
        self.assertTrue(any('accepted requires' in e for e in self.validator.validate_contract(c, self.scenarios)))

    def test_search_boundaries_and_unknowns_cannot_disappear(self):
        self.check_bad(lambda c, s: c['boundaries'].update(unknowns=[]), 'unknowns')
        self.check_bad(lambda c, s: c['boundaries'].update(inspected_sources=[]), 'inspected_sources')

    def test_scenario_shapes_and_required_fault_families(self):
        self.check_bad(lambda c, s: s.update(schema_version=2), 'schema_version')
        self.check_bad(lambda c, s: s['scenarios'][0].update(mode='passed'), 'mode')
        self.check_bad(lambda c, s: s['scenarios'][0].update(expected=[]), 'expected')
        self.check_bad(lambda c, s: s.update(scenarios=[x for x in s['scenarios'] if x['kind'] != 'link']), 'fixture famil')


if __name__ == '__main__':
    unittest.main()
