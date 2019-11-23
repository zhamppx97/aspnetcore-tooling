/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { EOL } from 'os';
import { ITokenizedContent } from './ITokenizedContent';

export function createSnapshot(tokenizedFile: ITokenizedContent): string {
    const snapshotLines: string[] = [];
    for (let i = 0; i < tokenizedFile.tokenizedLines.length; i++) {
        const line = tokenizedFile.lines[i];
        const tokenizedLine = tokenizedFile.tokenizedLines[i];

        snapshotLines.push(`Line: ${line[i]}`);
        for (const token of tokenizedLine.tokens) {
            snapshotLines.push(` - token from ${token.startIndex} to ${token.endIndex} ` +
                `(${line.substring(token.startIndex, token.endIndex)}) ` +
                `with scopes ${token.scopes.join(', ')}`);
        }
        snapshotLines.push('');
    }

    const snapshot = snapshotLines.join(EOL);
    return snapshot;
}
