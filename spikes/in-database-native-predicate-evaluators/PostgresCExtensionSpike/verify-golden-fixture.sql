-- Verifies decrypt_and_compare_c (this extension) and decrypt_and_compare
-- (the sibling plpython3u function) against the exact same golden
-- EnvelopeAesGcm ciphertext fixture tests/EventStore.SqlClr.SqlServer.Tests/
-- EncryptedPredicateFunctionsTests.cs already uses for the SQL Server side
-- -- expected result for every column below is t,f,t,t,f,t,f,t,f (the
-- identical sequence that test file's own assertions check).
\set key '''4fs+TJWaTTE9sx19HWvqYYXWPY072Nm/32mJxqFCYD0='''
\set numct '''LVU6ANGl+u5gD7TQb2aYy0bvGOFgUmVEAECvpP7ShJautA=='''
\set datect '''ljAzgGlMwjhP43L9INTKooy/xc4xmqiSUWBnDso6XRQTFsiKEWB6mmYjg1ufdIQ4yWo='''

SELECT 'C extension' AS impl,
  decrypt_and_compare_c(:numct, decode(:key,'base64'), 'Number', 'gt', '40') AS num_gt_40,
  decrypt_and_compare_c(:numct, decode(:key,'base64'), 'Number', 'gt', '50') AS num_gt_50,
  decrypt_and_compare_c(:numct, decode(:key,'base64'), 'Number', 'gte', '42.5') AS num_gte_425,
  decrypt_and_compare_c(:numct, decode(:key,'base64'), 'Number', 'lt', '100') AS num_lt_100,
  decrypt_and_compare_c(:numct, decode(:key,'base64'), 'Number', 'lte', '10') AS num_lte_10,
  decrypt_and_compare_c(:datect, decode(:key,'base64'), 'DateTimeOffset', 'gt', '2026-01-01T00:00:00Z') AS date_gt_jan1,
  decrypt_and_compare_c(:datect, decode(:key,'base64'), 'DateTimeOffset', 'gt', '2026-12-01T00:00:00Z') AS date_gt_dec1,
  decrypt_and_compare_c(:datect, decode(:key,'base64'), 'DateTimeOffset', 'lte', '2026-03-15T00:00:00Z') AS date_lte_mar15,
  decrypt_and_compare_c(:numct, decode('0000000000000000000000000000000000000000000000000000000000000000','hex'), 'Number', 'gt', '0') AS wrong_key;

SELECT 'plpython3u' AS impl,
  decrypt_and_compare(:numct, decode(:key,'base64'), 'Number', 'gt', '40') AS num_gt_40,
  decrypt_and_compare(:numct, decode(:key,'base64'), 'Number', 'gt', '50') AS num_gt_50,
  decrypt_and_compare(:numct, decode(:key,'base64'), 'Number', 'gte', '42.5') AS num_gte_425,
  decrypt_and_compare(:numct, decode(:key,'base64'), 'Number', 'lt', '100') AS num_lt_100,
  decrypt_and_compare(:numct, decode(:key,'base64'), 'Number', 'lte', '10') AS num_lte_10,
  decrypt_and_compare(:datect, decode(:key,'base64'), 'DateTimeOffset', 'gt', '2026-01-01T00:00:00Z') AS date_gt_jan1,
  decrypt_and_compare(:datect, decode(:key,'base64'), 'DateTimeOffset', 'gt', '2026-12-01T00:00:00Z') AS date_gt_dec1,
  decrypt_and_compare(:datect, decode(:key,'base64'), 'DateTimeOffset', 'lte', '2026-03-15T00:00:00Z') AS date_lte_mar15,
  decrypt_and_compare(:numct, decode('0000000000000000000000000000000000000000000000000000000000000000','hex'), 'Number', 'gt', '0') AS wrong_key;
