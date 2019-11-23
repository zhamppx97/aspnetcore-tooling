/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { createSnapshot } from './infrastructure/SnapshotFactory';
import { tokenize } from './infrastructure/TokenizedContentProvider';

describe('Transitions', () => {
    it('Escaped transitions', async () => {
        const tokenizedContent = await tokenize('@@');
        const currentSnapshot = createSnapshot(tokenizedContent);
        expect(currentSnapshot).toMatchSnapshot();
    });
});
