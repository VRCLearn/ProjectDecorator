"""Check archive contents, asset identity, migration scope and release provenance."""
import hashlib
import json
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import build_package as builder
import check_existing_release
import prepare_release


class DistributionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.package = self.root / "source"
        self.package.mkdir()
        self.manifest = json.loads((builder.ROOT / "package.json").read_text(encoding="utf-8"))
        for filename in builder.ROOT_FILES:
            (self.package / filename).write_bytes((builder.ROOT / filename).read_bytes())
            (self.package / (filename + ".meta")).write_bytes((builder.ROOT / (filename + ".meta")).read_bytes())
        self.asset("Editor", folder=True)
        self.asset("Editor/Empty", folder=True)
        self.asset("Editor/Example.cs", b"// Package fixture\n")
        for module in (builder, prepare_release):
            patcher = patch.object(module, "PACKAGE", self.package)
            patcher.start()
            self.addCleanup(patcher.stop)

    def asset(self, relative, content=b"", folder=False):
        path = self.package / relative
        if folder:
            path.mkdir(parents=True, exist_ok=True)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        path.with_name(path.name + ".meta").write_bytes(builder.generated_metadata(relative, folder))
        return path

    def write_manifest(self, **overrides):
        manifest = dict(self.manifest, **overrides)
        (self.package / "package.json").write_text(json.dumps(manifest), encoding="utf-8")

    def build(self, directory="release"):
        return builder.build(self.root / directory, repository=builder.REPOSITORY, revision="ab" * 20)

    def test_archives_preserve_asset_guids_and_empty_folders_without_source_changes(self):
        original = {path.relative_to(self.package).as_posix(): path.read_bytes()
                    for path in self.package.rglob("*") if path.is_file()}
        result = self.build()
        manifest = json.loads(Path(result["manifest"]).read_bytes())
        self.assertEqual(manifest["sourceRevision"], "ab" * 20)
        self.assertEqual(manifest["repository"]["revision"], "ab" * 20)
        self.assertNotIn("legacyPackages", manifest)
        self.assertNotIn("legacyFiles", manifest)
        self.assertEqual(manifest["legacyFolders"], {builder.UNITY_ROOT: builder.ROOT_GUID})
        self.assertNotIn("zipSHA256", manifest)
        zipped = builder.validate_zip(Path(result["archive"]), manifest)
        unitypackage = Path(result["unitypackage"])
        with unitypackage.open("rb") as archive:
            header = archive.read(10)
            self.assertTrue(header[3] & 0x08)
            self.assertEqual(archive.read(len(b"archtemp.tar\0")), b"archtemp.tar\0")
        imported = builder.validate_unitypackage(unitypackage, manifest)
        with zipfile.ZipFile(result["archive"]) as archive:
            self.assertIn("Editor/Empty/", archive.namelist())
        for relative, content in original.items():
            if relative != "package.json":
                self.assertEqual(zipped[relative], content)
        asset = imported[builder.UNITY_ROOT + "/Editor/Example.cs"]
        self.assertEqual(asset["asset.meta"], original["Editor/Example.cs.meta"])
        self.assertEqual(asset["asset"], original["Editor/Example.cs"])
        self.assertEqual(original, {path.relative_to(self.package).as_posix(): path.read_bytes()
                                    for path in self.package.rglob("*") if path.is_file()})

    def test_build_is_reproducible_and_checksums_match(self):
        first, second = self.build("first"), self.build("second")
        for name in ("archive", "unitypackage", "manifest"):
            self.assertEqual(Path(first[name]).read_bytes(), Path(second[name]).read_bytes())
        checksums = (self.root / "first/SHA256SUMS.txt").read_text(encoding="utf-8")
        for name in ("archive", "unitypackage", "manifest"):
            path = Path(first[name])
            self.assertIn(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name, checksums)

    def test_excludes_development_and_unity_ignored_names_with_their_metadata(self):
        for relative in ("Editor/.hidden", "Editor/Debug~", "Editor/__pycache__",
                         "Editor/CVS", "Editor/Scratch.tmp"):
            self.asset(relative, folder=True)
            self.asset(relative + "/Ignored.cs", b"// excluded")
        for directory in (".github", "Tests~", "UserSettings", "Library", "Logs"):
            (self.package / directory).mkdir()
            (self.package / directory / "secret.txt").write_text("excluded", encoding="utf-8")
        result = self.build()
        with zipfile.ZipFile(result["archive"]) as archive:
            names = archive.namelist()
        self.assertFalse(any(any(word in name for word in ("hidden", "Debug", "pycache", "CVS", "Scratch", ".github", "Tests~", "UserSettings", "Library", "Logs")) for name in names))

    def test_missing_metadata_and_duplicate_guids_fail_before_output(self):
        path = self.package / "Editor/Example.cs.meta"
        content = path.read_bytes()
        path.unlink()
        with self.assertRaisesRegex(ValueError, "missing existing Unity metadata"):
            self.build()
        path.write_bytes(content)
        other = self.asset("Editor/Other.cs")
        other.with_name(other.name + ".meta").write_bytes(content)
        with self.assertRaisesRegex(ValueError, "duplicate GUID"):
            self.build()
        self.assertFalse((self.root / "release").exists())

    def test_rejects_wrong_identity_license_migration_and_stale_download_url(self):
        cases = ({"name": "another.package"}, {"license": "unlicensed"}, {"version": "v1.0.0"},
                 {"vpmDependencies": {"com.vrchat.avatars": "1.0.0"}},
                 {"legacyPackages": ["another.package"]},
                 {"legacyPackages": []}, {"legacyFiles": {}}, {"zipSHA256": "a" * 64},
                 {"legacyFolders": {"Assets/HierarchyDecorator": "a" * 32}},
                 {"legacyFolders": {builder.UNITY_ROOT: "a" * 32}},
                 {"url": "https://example.test/other.zip"})
        for override in cases:
            with self.subTest(override=override):
                self.write_manifest(**override)
                with self.assertRaises(ValueError):
                    self.build()

    def test_rejects_missing_license_notice_and_source_revision(self):
        for repository, revision in (("other/project", "ab" * 20), (builder.REPOSITORY, None),
                                     (builder.REPOSITORY, "short")):
            with self.subTest(repository=repository, revision=revision), self.assertRaises(ValueError):
                builder.build(self.root / "release", repository=repository, revision=revision)
        for filename, message in (("LICENSE-MIT.md", "MIT license"),
                                  ("LICENSE-APACHE.md", "Apache License"),
                                  ("NOTICE.md", "NOTICE")):
            path = self.package / filename
            original = path.read_bytes()
            path.write_text("Incomplete", encoding="utf-8")
            with self.subTest(filename=filename), self.assertRaisesRegex(ValueError, message):
                self.build()
            path.write_bytes(original)
        self.assertFalse((self.root / "release").exists())

    def test_requires_the_author_name_and_email_for_vpm(self):
        for author in ({"name": "VRCLearn"}, {"name": "VRCLearn", "email": "invalid"},
                       {"name": "VRCLearn", "email": None}, {"name": "", "email": "a@example.test"},
                       {"name": 12, "email": "a@example.test"}, "VRCLearn"):
            self.write_manifest(author=author)
            with self.subTest(author=author), self.assertRaisesRegex(ValueError, "author"):
                self.build()

    def test_rejects_build_output_inside_source_assets(self):
        for output in (self.package, self.package / "Editor/Output"):
            with self.subTest(output=output), self.assertRaisesRegex(ValueError, "overwrite"):
                builder.build(output, repository=builder.REPOSITORY, revision="ab" * 20)

    def test_release_uses_exact_version_notes_and_rejects_other_tags(self):
        version = self.manifest["version"]
        release_text = "### Changes\n\n- Replace the former settings layout.\n- Keep custom icons."
        (self.package / "CHANGELOG.md").write_text(
            "## Unreleased\n\n- Future work.\n\n## [" + version + "] - 2026-10-19\n\n" +
            release_text + "\n\n## [0.0.1]\n\n- Previous release.\n", encoding="utf-8")
        for tag in ("v" + version, "0.12.1", "main", None):
            with self.subTest(tag=tag), self.assertRaisesRegex(ValueError, "tag"):
                prepare_release.prepare(self.root / "release", tag, builder.REPOSITORY, "ab" * 20)
        prepare_release.prepare(self.root / "release", version, builder.REPOSITORY, "ab" * 20)
        notes = (self.root / "release/release-notes.md").read_text(encoding="utf-8")
        self.assertEqual(notes, f"ProjectDecorator {version}\n\n" + release_text + "\n")
        (self.package / "CHANGELOG.md").write_text("## Unreleased\n\n- Future work.\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Changelog"):
            prepare_release.prepare(self.root / "missing-notes", version, builder.REPOSITORY, "ab" * 20)
        self.assertFalse((self.root / "missing-notes").exists())

    def test_release_asset_and_assembly_identities_are_standalone(self):
        with patch.object(builder, "PACKAGE", builder.ROOT):
            assets = builder.source_assets(self.manifest)
        definitions = [json.loads(asset.content) for asset in assets if asset.path.endswith(".asmdef")]
        self.assertEqual(len(definitions), 1)
        definition = definitions[0]
        self.assertEqual(definition["name"], "VRCLearn.ProjectDecorator.Editor")
        self.assertEqual(definition["rootNamespace"], "VRCLearn.ProjectDecorator.Editor")
        self.assertEqual(definition["includePlatforms"], ["Editor"])
        self.assertEqual(definition.get("references", []), [])
        guids = [builder.metadata_guid(asset.metadata) for asset in assets]
        self.assertEqual(len(guids), len(set(guids)))
        self.assertEqual(builder.ROOT_GUID, "cdd6638bd5e95314bba45469ca9c0bb2")
        self.assertNotIn("legacyPackages", self.manifest)
        self.assertNotIn("legacyFiles", self.manifest)

    def test_csharp_namespaces_are_standalone(self):
        for source in (builder.ROOT / "Editor").rglob("*.cs"):
            namespaces = re.findall(r"^\s*namespace\s+([\w.]+)", source.read_text(encoding="utf-8-sig"), re.MULTILINE)
            with self.subTest(source=source.relative_to(builder.ROOT).as_posix()):
                self.assertTrue(namespaces, "Each package source must use an explicit namespace")
                self.assertTrue(all(name == "VRCLearn.ProjectDecorator.Editor" or
                                    name.startswith("VRCLearn.ProjectDecorator.Editor.") for name in namespaces), namespaces)

    def test_existing_release_requires_complete_matching_assets(self):
        result = self.build()
        output = Path(result["manifest"]).parent
        paths = [Path(result["archive"]), Path(result["unitypackage"]), output / "package.json", output / "SHA256SUMS.txt"]
        assets = [{"name": path.name, "state": "uploaded", "size": path.stat().st_size,
                   "digest": "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()} for path in paths]
        release = {"tag_name": self.manifest["version"], "draft": False, "prerelease": False, "assets": assets}
        check_existing_release.validate(release, output)
        for override in ({"draft": True}, {"prerelease": True}, {"tag_name": "0.12.1"}, {"assets": assets[:-1]}):
            with self.subTest(override=override), self.assertRaises(ValueError):
                check_existing_release.validate(dict(release, **override), output)
        assets[0]["digest"] = "sha256:" + "0" * 64
        with self.assertRaisesRegex(ValueError, "digest"):
            check_existing_release.validate(release, output)


if __name__ == "__main__":
    unittest.main()
